using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>Planning and recovery levels only; execution must not re-cap an assigned target.</summary>
    public static class AutonomousGroupTargetPolicy
    {
        public static int PreferredBonus(int groupSize) => Math.Clamp(groupSize, 2, 8) switch
        {
            <= 3 => 1,
            <= 6 => 2,
            _ => 3,
        };

        public static int PreferredLevel(int averageLevel, int groupSize, int wipePenalty = 0) =>
            Math.Max(1, averageLevel + PreferredBonus(groupSize) - Math.Max(0, wipePenalty));

        public static int PenaltyAfterWipe(int averageLevel, int groupSize, int oldPenalty, int selectedLevel)
        {
            int previousTarget = selectedLevel > 0 ? selectedLevel : PreferredLevel(averageLevel, groupSize, oldPenalty);
            return Math.Clamp(Math.Max(oldPenalty + 1,
                PreferredLevel(averageLevel, groupSize) - Math.Max(1, previousTarget - 1)), 0, 50);
        }

        // Exact preferred level first, then closest easier level. If repeated
        // deaths request a level below every XP-bearing mob, take the lowest
        // valid level available, never above the original size-based ceiling.
        // Caller has already applied realm, live-spawn, reachability and grey checks.
        public static int SelectAvailableLevel(IEnumerable<int> levels, int averageLevel, int groupSize, int wipePenalty = 0)
        {
            int preferred = PreferredLevel(averageLevel, groupSize, wipePenalty);
            int ceiling = PreferredLevel(averageLevel, groupSize);
            int best = 0;
            int lowest = int.MaxValue;
            foreach (int level in levels)
            {
                if (level < 1 || level > ceiling) continue;
                lowest = Math.Min(lowest, level);
                if (level <= preferred) best = Math.Max(best, level);
            }
            return best > 0 ? best : lowest == int.MaxValue ? 0 : lowest;
        }

        public static int SelectFixedEightManLevel(IEnumerable<int> levels, int averageLevel, int rolledBonus, int groupSize = 8)
        {
            int minimum = averageLevel + 3;
            int maximum = averageLevel + Math.Clamp(rolledBonus, 3, 10);
            int[] all = (levels ?? Array.Empty<int>()).ToArray();
            // Below level 10 one level is a whole con colour, so +3 is already purple: groups of level-1 bots went for
            // level-6 monsters and wiped over and over (owner 2026-10-31 run: one bot died 36 times in an hour). There the
            // target is capped at the group challenge colour the experience split uses (2-3 yellow, 4-7 orange, 8 red),
            // the highest level that fits; level 10 and up keep the +3..+10 rule unchanged.
            if (averageLevel < 10)
            {
                ConColor cap = LowLevelGroupCap(groupSize);
                ConColor Con(int level) => ConLevels.GetConColor(ConLevels.GetConLevel(averageLevel, level));
                int capped = 0;
                foreach (int level in all)
                    if (level >= minimum && level <= maximum && Con(level) <= cap)
                        capped = Math.Max(capped, level);
                if (capped == 0)
                    foreach (int level in all)
                        if (Con(level) >= ConColor.YELLOW && Con(level) <= cap)
                            capped = Math.Max(capped, level);
                return capped;
            }
            int best = 0;
            foreach (int level in all)
                if (level >= minimum && level <= maximum)
                    best = Math.Max(best, level);
            return best;
        }

        /// <summary>The group challenge colour a party below level 10 aims for (2-3 yellow, 4-7 orange, 8 red).</summary>
        public static ConColor LowLevelGroupCap(int groupSize) =>
            groupSize >= 8 ? ConColor.RED : groupSize >= 4 ? ConColor.ORANGE : ConColor.YELLOW;

        /// <summary>
        /// A camp level a party may plan for: never grey to its highest member, and inside the window
        /// SelectFixedEightManLevel picks from. Below level 10 that is yellow up to the group cap; the old +3 floor
        /// there kept only purple levels, so the selector found none and low-level parties got no camp or a
        /// level-0 target (2026-11-06 run: 13 of 17 low-level group tasks ended "No reachable non-grey group camp").
        /// </summary>
        public static bool IsGroupCampLevel(int level, int averageLevel, int rolledBonus, int highestMemberLevel, int groupSize)
        {
            if (ConLevels.GetConColor(ConLevels.GetConLevel(highestMemberLevel, level)) <= ConColor.GREY)
                return false;
            if (averageLevel < 10)
            {
                ConColor con = ConLevels.GetConColor(ConLevels.GetConLevel(averageLevel, level));
                return con >= ConColor.YELLOW && con <= LowLevelGroupCap(groupSize);
            }
            return level >= averageLevel + 3 && level <= averageLevel + rolledBonus;
        }
    }
}
