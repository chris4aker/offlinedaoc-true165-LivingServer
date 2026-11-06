using System;
using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode: one solo gamebot's deaths beside its own bind point (2026-11-06: 18 Midgard bots bound at
    /// 805790,701507-701902 in region 100 averaged 67-79 deaths, twice the realm average; level 6-7 hill persons a short
    /// walk away killed them again right after each release). After <see cref="DeathsBeforeMove"/> deaths within
    /// <see cref="Radius"/> of the bind inside <see cref="WindowMilliseconds"/> the bot binds elsewhere, as a player would:
    /// at the nearest friendly bindstone at least <see cref="Clearance"/> from every bind it left this way, or in its
    /// capital. Monsters are not touched.
    /// </summary>
    public sealed class AutonomousBindDeaths
    {
        public const int Radius = 2_500;
        public const int DeathsBeforeMove = 3;
        public const long WindowMilliseconds = 15 * 60_000;
        public const int Clearance = 5_000;
        private const int MaxLeftBinds = 8;

        private readonly long[] _ticks = new long[DeathsBeforeMove];
        private int _next;
        private readonly List<(ushort Region, int X, int Y)> _left = new();

        /// <summary>Records a death; only one within <see cref="Radius"/> of the bind counts.</summary>
        public void Record(ushort deathRegion, int x, int y, ushort bindRegion, int bindX, int bindY, long now)
        {
            if (deathRegion != bindRegion || !Near(x, y, bindX, bindY, Radius))
                return;
            _ticks[_next] = now;
            _next = (_next + 1) % _ticks.Length;
        }

        /// <summary>True when the last <see cref="DeathsBeforeMove"/> deaths were all beside the bind within the window.</summary>
        public bool ShouldMove(long now)
        {
            foreach (long tick in _ticks)
                if (tick <= 0 || now - tick > WindowMilliseconds)
                    return false;
            return true;
        }

        /// <summary>The bot left this bind: it is avoided from now on and the count starts over.</summary>
        public void Moved(ushort region, int x, int y)
        {
            Array.Clear(_ticks);
            _next = 0;
            if (_left.Count >= MaxLeftBinds)
                _left.RemoveAt(0);
            _left.Add((region, x, y));
        }

        /// <summary>True for a bindstone too close to a bind the bot already left.</summary>
        public bool Avoids(ushort region, int x, int y)
        {
            foreach (var left in _left)
                if (left.Region == region && Near(x, y, left.X, left.Y, Clearance))
                    return true;
            return false;
        }

        private static bool Near(int ax, int ay, int bx, int by, int radius)
        {
            double dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy <= (double)radius * radius;
        }
    }
}
