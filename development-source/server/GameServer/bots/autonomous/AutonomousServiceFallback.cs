using System.Collections.Concurrent;

namespace DOL.GS
{
    /// <summary>
    /// What a gamebot does when the service it needs has no reachable NPC (owner 2026-11-08). Overnight 11/07, 294 bots
    /// of level 10+ stood "Waiting for a real world service" for 15 minutes each (603 watchdog teleports): each realm
    /// has one Realm Exchange broker, and its only broker was refused (a death hotspot on the straight way there, or the
    /// bot had died on the way and skipped it), so the search found nothing and the bot idled instead of selling.
    /// Now, like a player: no broker in reach means the bag is sold at the nearest merchant (vendor trash first; when
    /// the bag stays nearly full, the items it was keeping for the exchange too), and no merchant either means the bot
    /// carries on with its next task. A bot never waits on a service with no target.
    /// </summary>
    public static class AutonomousServiceFallback
    {
        /// <summary>How long a bot that found no reachable broker sells at merchants instead (owner 2026-11-08).</summary>
        public const long ExchangeUnreachableMilliseconds = 20 * 60_000;
        private const int MaxEntries = 16_384;

        private static readonly ConcurrentDictionary<long, long> ExchangeUnreachableUntil = new();
        private static int _count;

        /// <summary>Pure rule: a bot keeps loot for the exchange only while a broker is in reach; once none is, items
        /// worth listing are sold too, but only while the bag is still nearly full (the rest are kept).</summary>
        public static bool KeepsForExchange(bool canList, bool exchangeUnreachable, bool backpackNearlyFull) =>
            canList && !(exchangeUnreachable && backpackNearlyFull);

        /// <summary>Owner 2026-11-10: real checks in a row at the broker (one every 3-7 s) that found nothing to list or buy
        /// before the bot gives the exchange up for <see cref="ExchangeUnreachableMilliseconds"/>.</summary>
        public const int FruitlessExchangeChecksBeforeGivingUp = 3;

        /// <summary>Pure rule (owner 2026-11-10): the exchange step ends after
        /// <see cref="FruitlessExchangeChecksBeforeGivingUp"/> fruitless checks in a row, never while a listing waits.</summary>
        public static bool GivesUpOnExchange(int fruitlessChecks, bool listingPending) =>
            !listingPending && fruitlessChecks >= FruitlessExchangeChecksBeforeGivingUp;

        public static void MarkExchangeUnreachable(long botId, long now)
        {
            if (botId <= 0)
                return;
            // the size is only looked at when a bot is added (Count locks the whole dictionary)
            if (!ExchangeUnreachableUntil.ContainsKey(botId) && System.Threading.Interlocked.Increment(ref _count) > MaxEntries)
            {
                ExchangeUnreachableUntil.Clear();
                System.Threading.Interlocked.Exchange(ref _count, 1);
            }
            ExchangeUnreachableUntil[botId] = now + ExchangeUnreachableMilliseconds;
        }

        public static bool IsExchangeUnreachable(long botId, long now) =>
            botId > 0 && ExchangeUnreachableUntil.TryGetValue(botId, out long until) && now < until;

        public static void Forget(long botId)
        {
            if (ExchangeUnreachableUntil.TryRemove(botId, out _))
                System.Threading.Interlocked.Decrement(ref _count);
        }

        /// <summary>Tests only.</summary>
        public static void Clear()
        {
            ExchangeUnreachableUntil.Clear();
            System.Threading.Interlocked.Exchange(ref _count, 0);
        }
    }
}
