using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>Short, group-local in-flight claims, not permanent buff assignments.</summary>
    public sealed class BotBuffReservations<T> where T : class
    {
        private readonly object _gate = new();
        private readonly Dictionary<(T Target, int Family), (T Caster, long Until)> _claims = new();

        public bool IsReserved(T target, int family, long now)
        {
            lock (_gate)
                return _claims.TryGetValue((target, family), out var claim) && claim.Until > now;
        }

        public bool TryReserve(T caster, T target, int family, long now, int castTime)
        {
            lock (_gate)
            {
                foreach (var key in _claims.Where(entry => entry.Value.Until <= now).Select(entry => entry.Key).ToArray())
                    _claims.Remove(key);
                if (_claims.ContainsKey((target, family))) return false;
                _claims[(target, family)] = (caster, now + Math.Clamp((long)castTime + 2000, 2000, 15000));
                return true;
            }
        }

        public void Release(T caster, T target, int family)
        {
            lock (_gate)
                if (_claims.TryGetValue((target, family), out var claim) && ReferenceEquals(claim.Caster, caster))
                    _claims.Remove((target, family));
        }

        public static T Choose(IEnumerable<T> candidates, Func<T, bool> eligible, Func<int, int> roll = null)
        {
            T result = null;
            int count = 0;
            foreach (T candidate in candidates)
                if (candidate != null && eligible(candidate) && (roll ?? Random.Shared.Next)(++count) == 0)
                    result = candidate;
            return result;
        }

        /// <summary>
        /// The eligible candidate with the lowest rank, at random among equals. The rank is read first, so a worse-ranked
        /// candidate is never checked for eligibility once a better one was found.
        /// </summary>
        public static T ChooseBest(IEnumerable<T> candidates, Func<T, bool> eligible, Func<T, int> rank, Func<int, int> roll = null)
        {
            T result = null;
            int best = int.MaxValue, count = 0;
            foreach (T candidate in candidates)
            {
                if (candidate == null)
                    continue;
                int candidateRank = rank(candidate);
                if (candidateRank > best || !eligible(candidate))
                    continue;
                if (candidateRank < best)
                {
                    best = candidateRank;
                    count = 0;
                }
                if ((roll ?? Random.Shared.Next)(++count) == 0)
                    result = candidate;
            }
            return result;
        }
    }
}
