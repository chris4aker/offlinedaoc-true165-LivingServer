using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// The saved-bot cap (plan 3.4, population_saved_cap, default 1,000,000). Below the cap nothing is ever deleted.
    /// Once the cap is reached, batches of 16 random offline bots (any level) are retired for good every 2-5 s down to
    /// 99% of the cap, then retirement only matches the new arrivals. Level 50s have a quarter of the odds and a reserve
    /// of offline 50s (1.5x each realm's live level-50 target) is never touched. Never: hired companions, the player's
    /// guildmates, bots online or logging in. Runs on its own background timer; CORE's Retire does the deleting.
    /// </summary>
    public sealed class PopulationRetirement
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static void LogInfo(string message) { if (Log.IsInfoEnabled) Log.Info(message); }
        private static void LogWarn(string message) { if (Log.IsWarnEnabled) Log.Warn(message); }
        private static void LogError(string message, Exception exception) { if (Log.IsErrorEnabled) Log.Error(message, exception); }

        public const int BatchSize = 16;
        public const double TargetFraction = 0.99;
        public const double Level50Odds = 0.25;
        public const double ReserveFactor = 1.5;
        public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(30);

        private readonly IPopulationArchive _archive;
        private readonly IPopulationLifecycle _lifecycle;
        private readonly IPopulationWorld _world;
        private readonly Func<long, bool> _isBusy;
        private readonly Func<eRealm, int> _live50Target;
        private readonly Func<long> _savedCap;
        private readonly IPopulationClock _clock;
        private readonly Random _rng;
        private readonly Action<eRealm, IReadOnlyCollection<long>> _onRetired;
        private bool _firstLook = true;
        // CORE's Retire is queued to a worker, so the saved count drops a little later: count those as gone meanwhile.
        private long _inFlight;
        private long _lastSaved = -1;
        private DateTime _lastRetireUtc = DateTime.MinValue;
        public static readonly TimeSpan InFlightForget = TimeSpan.FromSeconds(60);

        public PopulationRetirement(IPopulationArchive archive, IPopulationLifecycle lifecycle, IPopulationWorld world,
            Func<long, bool> isBusy, Func<eRealm, int> live50Target, Func<long> savedCap, IPopulationClock clock, Random rng,
            Action<eRealm, IReadOnlyCollection<long>> onRetired)
        {
            _archive = archive ?? throw new ArgumentNullException(nameof(archive));
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _isBusy = isBusy ?? (_ => false);
            _live50Target = live50Target ?? (_ => 0);
            _savedCap = savedCap ?? (() => 1_000_000);
            _clock = clock ?? SystemPopulationClock.Instance;
            _rng = rng ?? new Random();
            _onRetired = onRetired;
        }

        /// <summary>True once the cap was reached this run: from then on the saved count is kept at 99% of the cap.</summary>
        public bool Engaged { get; private set; }
        public DateTime NextBatchUtc { get; private set; } = DateTime.MinValue;
        public long RetiredTotal { get; private set; }

        public static long TargetFor(long cap) => (long)Math.Floor(cap * TargetFraction);

        /// <summary>Offline level 50s of a realm that are never retired.</summary>
        public static long ReserveFor(int live50Target) => (long)Math.Ceiling(ReserveFactor * Math.Max(0, live50Target));

        /// <summary>The checks that need no odds: never hired, guildmates of the player, online, logging in or queued to.</summary>
        public bool IsProtected(long botId) =>
            botId <= 0 || _world.IsHired(botId) || _world.IsPlayerGuildmate(botId) || _isBusy(botId) || _lifecycle.IsOnline(botId);

        /// <summary>Called every second from the background timer; does a batch when one is due. Returns how many it retired.</summary>
        public int Tick()
        {
            DateTime now = _clock.UtcNow;
            if (now < NextBatchUtc) return 0;
            NextBatchUtc = now + MinGap + TimeSpan.FromMilliseconds(_rng.NextDouble() * (MaxGap - MinGap).TotalMilliseconds);
            try
            {
                return Batch(now);
            }
            catch (Exception exception)
            {
                NextBatchUtc = now + ErrorBackoff;
                LogError("Bot retirement batch failed", exception);
                return 0;
            }
        }

        private int Batch(DateTime now)
        {
            long cap = _savedCap();
            if (cap <= 0) return 0;
            long saved = _archive.SavedCount;
            if (_lastSaved >= 0 && saved < _lastSaved) _inFlight = Math.Max(0, _inFlight - (_lastSaved - saved));
            if (now - _lastRetireUtc >= InFlightForget) _inFlight = 0;
            _lastSaved = saved;
            saved -= _inFlight;
            long target = TargetFor(cap);
            // A restart while already at 99% carries on matching inflow instead of waiting for the full cap again.
            if (!Engaged && (saved >= cap || (_firstLook && saved > target))) Engaged = true;
            _firstLook = false;
            if (!Engaged || saved <= target) return 0;

            int count = (int)Math.Min(BatchSize, saved - target);
            var spare50 = new Dictionary<eRealm, long>();
            foreach (eRealm realm in AutonomousPopulationScheduler.Realms)
                spare50[realm] = _archive.Count(realm, 50, 50) - ReserveFor(_live50Target(realm));
            var realmOf = new Dictionary<long, eRealm>();

            bool Eligible(ArchiveCandidate c)
            {
                if (c == null || IsProtected(c.BotId)) return false;
                if (c.Level >= 50)
                {
                    if (_rng.NextDouble() >= Level50Odds) return false;          // a quarter of the odds
                    if (!spare50.TryGetValue(c.Realm, out long spare) || spare <= 0) return false;   // the reserve
                    spare50[c.Realm] = spare - 1;
                }
                realmOf[c.BotId] = c.Realm;
                return true;
            }

            IReadOnlyList<long> picked = _archive.RandomRetireCandidates(count, Eligible) ?? Array.Empty<long>();
            // Checked again: a bot may have started logging in while the store was sampling.
            List<long> final = picked.Distinct().Where(id => !IsProtected(id)).Take(count).ToList();
            if (final.Count == 0) return 0;
            _archive.Retire(final);
            RetiredTotal += final.Count;
            _inFlight += final.Count;
            _lastRetireUtc = now;
            if (_onRetired != null)
            {
                foreach (IGrouping<eRealm, long> group in final.GroupBy(id => realmOf.TryGetValue(id, out eRealm r) ? r : eRealm.None))
                    _onRetired(group.Key, group.ToList());
            }
            LogInfo($"POPULATION_RETIRE retired={final.Count} saved={saved} cap={cap} target={target}");
            return final.Count;
        }
    }
}
