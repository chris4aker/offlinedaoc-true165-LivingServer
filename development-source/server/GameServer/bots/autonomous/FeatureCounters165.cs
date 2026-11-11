using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// FEATURE_COUNTERS_165 (2026-11-11): features that write nothing to the log when they work (banter lines per
    /// channel, upkeep held while an autonomous party walks, pet buffs skipped as useless, the healer's pre-HoT on the
    /// tank, heal-over-time stacking skips, guild dues from gamebots) are counted here and summed in one log line per
    /// five minutes, so an overnight run can confirm they fire. Cost: one Interlocked add per event; the line is written
    /// by the first event after the window ends, so nothing runs while nothing happens.
    /// </summary>
    public static class FeatureCounters165
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public const string Tag = "FEATURE_COUNTERS_165";
        public const long WindowMilliseconds = 5 * 60_000;

        private static readonly ConcurrentDictionary<string, long[]> Counts = new(StringComparer.Ordinal);
        private static long _windowStart = -1;

        /// <summary>Milliseconds clock (replaceable by tests).</summary>
        public static Func<long> Clock { get; set; } = () => Environment.TickCount64;

        /// <summary>Where the summary line goes (replaceable by tests).</summary>
        public static Action<string> Sink { get; set; } = line => Log.Info(line);

        public static void Add(string counter, long amount = 1)
        {
            if (string.IsNullOrEmpty(counter) || amount == 0)
                return;
            try
            {
                long[] cell = Counts.GetOrAdd(counter, static _ => new long[1]);
                Interlocked.Add(ref cell[0], amount);
                long now = Clock();
                long start = Interlocked.Read(ref _windowStart);
                if (start < 0)
                {
                    Interlocked.CompareExchange(ref _windowStart, now, start);
                    return;
                }
                // one thread wins the window end and writes the line; the others just count on
                if (now - start >= WindowMilliseconds && Interlocked.CompareExchange(ref _windowStart, now, start) == start)
                    Flush(now - start);
            }
            catch { } // diagnostics never break the feature that is counted
        }

        /// <summary>Writes the summary of the counts since the last line and resets them; null when nothing was counted.</summary>
        public static string Flush(long elapsedMilliseconds)
        {
            var line = new StringBuilder(Tag).Append(" window=").Append(Math.Max(0, elapsedMilliseconds / 1000)).Append('s');
            bool any = false;
            foreach (string key in Counts.Keys.OrderBy(key => key, StringComparer.Ordinal))
            {
                if (!Counts.TryGetValue(key, out long[] cell))
                    continue;
                long value = Interlocked.Exchange(ref cell[0], 0);
                if (value == 0)
                    continue;
                line.Append(' ').Append(key).Append('=').Append(value);
                any = true;
            }
            if (!any)
                return null;
            string text = line.ToString();
            Sink?.Invoke(text);
            return text;
        }

        /// <summary>Tests: clears the counts and the window.</summary>
        public static void Reset()
        {
            Counts.Clear();
            Interlocked.Exchange(ref _windowStart, -1);
        }
    }
}
