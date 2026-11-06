using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Limits on the route checks a bot's camp target search runs (2026-11-06: one bot off the mesh tested every candidate
    /// within 2,600 and then 5,000 units, each failing check falling back to dozens of corridor searches - 904 of them in
    /// one 3.4 s server stall). One search checks at most <see cref="MaxChecks"/> routes and stops after
    /// <see cref="FailuresBeforeStop"/> failures in a row; a failed target is not checked again for
    /// <see cref="UnreachableTargets.Milliseconds"/> while the bot and the monster stay where they were, so the next search
    /// goes on to the next candidates.
    /// </summary>
    public struct AutonomousCampTargetRouteChecks
    {
        public const int MaxChecks = 6;
        public const int FailuresBeforeStop = 3;

        private int _checks;
        private int _failures;

        public bool Stopped { get; private set; }

        /// <summary>True when another route check may run in this search.</summary>
        public bool TryBegin()
        {
            if (Stopped)
                return false;
            if (_checks >= MaxChecks)
            {
                Stopped = true;
                return false;
            }
            _checks++;
            return true;
        }

        /// <summary>Records a failed check; true when this failure stops the search (the caller repairs the bot's floor once).</summary>
        public bool Failed()
        {
            if (++_failures < FailuresBeforeStop || Stopped)
                return false;
            Stopped = true;
            return true;
        }

        public void Succeeded() => _failures = 0;

        /// <summary>One bot's camp targets whose route check failed, by object id.</summary>
        public sealed class UnreachableTargets
        {
            public const long Milliseconds = 45_000;
            public const int OriginMovement = 512;
            public const int TargetMovement = 64;
            private const int MaxEntries = 256;

            private readonly Dictionary<ushort, (long Until, Vector3 Origin, Vector3 Target)> _targets = new();
            private long _nextPruneTick;

            public int Count => _targets.Count;

            public bool IsCached(ushort id, Vector3 origin, Vector3 target, long now) =>
                _targets.TryGetValue(id, out var failure) && now < failure.Until &&
                Vector3.DistanceSquared(failure.Origin, origin) <= OriginMovement * OriginMovement &&
                Vector3.DistanceSquared(failure.Target, target) <= TargetMovement * TargetMovement;

            public void Add(ushort id, Vector3 origin, Vector3 target, long now)
            {
                Prune(now);
                if (_targets.Count >= MaxEntries)
                    _targets.Clear();
                _targets[id] = (now + Milliseconds, origin, target);
            }

            public void Remove(ushort id) => _targets.Remove(id);

            public void Prune(long now)
            {
                if (now < _nextPruneTick)
                    return;
                _nextPruneTick = now + 10_000;
                List<ushort> expired = null;
                foreach (var pair in _targets)
                    if (now >= pair.Value.Until)
                        (expired ??= new()).Add(pair.Key);
                if (expired != null)
                    foreach (ushort id in expired)
                        _targets.Remove(id);
            }
        }
    }
}
