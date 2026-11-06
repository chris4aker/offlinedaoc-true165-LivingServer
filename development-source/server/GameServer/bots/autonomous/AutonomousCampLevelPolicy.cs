using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>Keep period-authored camp locations, but never advertise a mob
/// level that is absent from the matching live spawns near that location.</summary>
public static class AutonomousCampLevelPolicy
{
    public static int[] ObservedAuthoredLevels(IEnumerable<int> authoredLevels, IEnumerable<int> liveLevels)
    {
        if (authoredLevels == null || liveLevels == null)
            return [];
        HashSet<int> authored = authoredLevels.Where(level => level > 0).ToHashSet();
        int[] observed = liveLevels.Where(level => level > 0)
            .Distinct().OrderBy(level => level).ToArray();
        int[] historicallyMatching = observed.Where(authored.Contains).ToArray();
        // Prefer matching period levels when the live camp has them. If this
        // server's authentic spawn is a different level, use its observed level
        // rather than deleting the entire otherwise reachable camp from goals.
        return historicallyMatching.Length > 0 ? historicallyMatching : observed;
    }

    /// <summary>
    /// The same choice of levels, but one entry per live spawn, so a camp's levels count its monsters: the solo rule
    /// "at least half of a camp's spawns are worth experience" (AutonomousSoloCampFloor.CampQualifies) counted distinct
    /// levels for catalog camps, so skeletal pawns listed at levels 1-2 with nearly every live pawn at level 1 passed as
    /// "1 of 2" for a level-4 bot, which then found only greys there and left (2026-11-06: 1,618 outleveled abandons of
    /// two Connacht pawn camps in one night).
    /// </summary>
    public static int[] ObservedAuthoredSpawnLevels(IEnumerable<int> authoredLevels, IEnumerable<int> liveLevels)
    {
        if (authoredLevels == null || liveLevels == null)
            return [];
        int[] live = liveLevels.Where(level => level > 0).ToArray();
        HashSet<int> chosen = ObservedAuthoredLevels(authoredLevels, live).ToHashSet();
        return live.Where(chosen.Contains).OrderBy(level => level).ToArray();
    }
}
