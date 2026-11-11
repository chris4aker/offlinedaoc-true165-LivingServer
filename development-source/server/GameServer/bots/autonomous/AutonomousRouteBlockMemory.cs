using System;
using System.Collections.Generic;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Owner 2026-11-10: route spots that several solo bots gave a camp up for ("Route blocked by X ... with no way around").
    /// 56% of solo Retargets (11-08..11-10) came within 800 units of a spot where another bot of about the same level had
    /// given up in the last 30 minutes: every bot learnt the blocker on its own and paid for the same trip again. Once
    /// <see cref="DistinctBots"/> different bots retarget within <see cref="Radius"/> in <see cref="WindowMilliseconds"/>,
    /// the spot is a block for <see cref="ActiveMilliseconds"/> (renewed by each new retarget there). Solo bots up to the
    /// highest level that gave up there then plan no camp at it or straight past it (only when another camp is left).
    /// Kept per region, published as snapshots read without a lock (like AutonomousRoadHotspots). Monsters are not touched.
    /// </summary>
    public static class AutonomousRouteBlockMemory
    {
        [ServerProperty("bots", "bot_route_block_memory",
            "Owner 2026-11-10: solo gamebots share route spots where 3 or more bots of their level gave a camp up in the last 30 minutes, and plan no camp at or straight past them (only while another camp is left).", true)]
        public static bool Enabled = true;

        public const int DistinctBots = 3;
        public const int Radius = 800;
        public const int RouteRadius = 900;
        public const int DestinationRadius = 1_200;
        public const long WindowMilliseconds = 30 * 60_000;
        public const long ActiveMilliseconds = 30 * 60_000;
        public const int MaxBlocksPerRegion = 32;
        private const int MaxRecords = 4_000;

        private readonly record struct Retarget(int X, int Y, long Bot, int Level, long Tick);

        /// <summary>A block; replaced, never changed, so readers need no lock.</summary>
        public sealed record Block(ushort Region, int X, int Y, int MaxBotLevel, long Until);

        private static readonly object Sync = new();
        private static readonly Dictionary<ushort, List<Retarget>> Records = new();
        private static int _recordCount;
        // blocks per region, a new dictionary published on every change
        private static volatile Dictionary<ushort, Block[]> _blocks = new();

        private static long _latestUntil;

        /// <summary>True while any block may still be live; a cheap first test before the per-region reads.</summary>
        public static bool AnyActive(long now) => System.Threading.Volatile.Read(ref _latestUntil) > now;

        /// <summary>Records a solo bot's Retarget; returns the block it made or renewed, if any.</summary>
        public static Block RecordRetarget(ushort region, int x, int y, long bot, int botLevel, long now, out bool created,
            out int bots)
        {
            created = false;
            bots = 0;
            lock (Sync)
            {
                if (!Records.TryGetValue(region, out List<Retarget> records))
                    Records[region] = records = new();
                _recordCount -= records.RemoveAll(r => now - r.Tick > WindowMilliseconds);
                if (_recordCount >= MaxRecords)
                {
                    foreach (List<Retarget> other in Records.Values)
                        _recordCount -= other.RemoveAll(r => now - r.Tick > WindowMilliseconds);
                    if (_recordCount >= MaxRecords && records.Count > 0)
                    {
                        records.RemoveAt(0);
                        _recordCount--;
                    }
                }
                records.Add(new Retarget(x, y, bot, botLevel, now));
                _recordCount++;
                HashSet<long> distinct = new();
                int maxLevel = 0;
                foreach (Retarget r in records)
                    if (Near(r.X, r.Y, x, y, Radius))
                    {
                        distinct.Add(r.Bot);
                        maxLevel = Math.Max(maxLevel, r.Level);
                    }
                bots = distinct.Count;
                if (distinct.Count < DistinctBots)
                    return null;

                Block[] current = _blocks.TryGetValue(region, out Block[] existing) ? existing : Array.Empty<Block>();
                List<Block> next = new(current.Length + 1);
                Block old = null;
                foreach (Block b in current)
                {
                    if (b.Until <= now)
                        continue;
                    if (old == null && Near(b.X, b.Y, x, y, Radius))
                        old = b;
                    else
                        next.Add(b);
                }
                created = old == null;
                Block block = new(region, old?.X ?? x, old?.Y ?? y, Math.Max(old?.MaxBotLevel ?? 0, maxLevel), now + ActiveMilliseconds);
                next.Add(block);
                if (next.Count > MaxBlocksPerRegion)
                {
                    // drop the ones that run out soonest
                    next.Sort((a, b) => b.Until.CompareTo(a.Until));
                    next.RemoveRange(MaxBlocksPerRegion, next.Count - MaxBlocksPerRegion);
                }
                Dictionary<ushort, Block[]> published = new(_blocks);
                published[region] = next.ToArray();
                _blocks = published;
                if (block.Until > System.Threading.Volatile.Read(ref _latestUntil))
                    System.Threading.Volatile.Write(ref _latestUntil, block.Until);
                return block;
            }
        }

        /// <summary>
        /// The live blocks in one region for a bot of this level (an empty array when none). Read once per destination
        /// region in a planning pass and handed to <see cref="Blocks(Block[], ushort, int, int, ushort, int, int)"/>.
        /// </summary>
        public static Block[] ActiveFor(int botLevel, ushort region, long now)
        {
            if (!_blocks.TryGetValue(region, out Block[] all) || all.Length == 0)
                return [];
            int count = 0;
            foreach (Block b in all)
                if (b.Until > now && botLevel <= b.MaxBotLevel)
                    count++;
            if (count == all.Length)
                return all;
            if (count == 0)
                return [];
            Block[] result = new Block[count];
            int i = 0;
            foreach (Block b in all)
                if (b.Until > now && botLevel <= b.MaxBotLevel)
                    result[i++] = b;
            return result;
        }

        public static bool Blocks(Block[] blocks, ushort fromRegion, int fromX, int fromY, ushort toRegion, int toX, int toY)
        {
            foreach (Block b in blocks)
                if (Blocks(b, fromRegion, fromX, fromY, toRegion, toX, toY))
                    return true;
            return false;
        }

        /// <summary>
        /// Pure rule: the destination is near the block, or (same region, the bot not already at the block) the straight
        /// way there passes it. The cheap destination test runs first, then a bounding box, then the segment test.
        /// </summary>
        public static bool Blocks(Block b, ushort fromRegion, int fromX, int fromY, ushort toRegion, int toX, int toY)
        {
            if (b.Region != toRegion)
                return false;
            if (Near(b.X, b.Y, toX, toY, DestinationRadius))
                return true;
            if (fromRegion != toRegion || Near(b.X, b.Y, fromX, fromY, Radius))
                return false;
            if (b.X < Math.Min(fromX, toX) - RouteRadius || b.X > Math.Max(fromX, toX) + RouteRadius ||
                b.Y < Math.Min(fromY, toY) - RouteRadius || b.Y > Math.Max(fromY, toY) + RouteRadius)
                return false;
            return AutonomousRoadHotspots.SegmentDistanceSquared(fromX, fromY, toX, toY, b.X, b.Y) <=
                   (double)RouteRadius * RouteRadius;
        }

        public static void Clear()
        {
            lock (Sync)
            {
                Records.Clear();
                _recordCount = 0;
                _blocks = new();
                System.Threading.Volatile.Write(ref _latestUntil, 0);
            }
        }

        private static bool Near(int ax, int ay, int bx, int by, int radius)
        {
            double dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy <= (double)radius * radius;
        }
    }
}
