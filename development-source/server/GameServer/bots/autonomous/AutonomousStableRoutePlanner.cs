using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS.Movement;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

public static class AutonomousStableRouteLifecycle
{
    public static bool ShouldComplete(
        bool departurePending,
        bool movingOnPath,
        bool hasCurrentPathPoint,
        bool reachedFinalWaypoint) =>
        !departurePending && !movingOnPath && !hasCurrentPathPoint && reachedFinalWaypoint;
}

/// <summary>Keep failed first walks out of repeated stable searches while the
/// same bot is still standing in the same small pocket. Movement or expiry
/// makes the route eligible for a fresh navmesh check.</summary>
public sealed class AutonomousStableBoardingFailureCache
{
    public const int MaximumNewProbesPerSearch = 2;
    private const int MaximumEntries = 512;
    private const int ReuseDistance = 8;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly object _lock = new();
    private readonly Dictionary<Key, Failure> _failures = new();

    public readonly record struct Probe(long BotDatabaseId, ushort BotObjectId,
        ushort RegionId, ushort ZoneId, Vector3 Boarding, Vector3 Source);

    private readonly record struct Key(long BotDatabaseId, ushort BotObjectId,
        ushort RegionId, ushort ZoneId, Vector3 Boarding);

    private readonly record struct Failure(Vector3 Source, DateTime ExpiresUtc);

    public bool WasRecentlyUnreachable(Probe probe, DateTime nowUtc)
    {
        var key = new Key(probe.BotDatabaseId, probe.BotObjectId, probe.RegionId,
            probe.ZoneId, probe.Boarding);
        lock (_lock)
        {
            if (!_failures.TryGetValue(key, out Failure failure))
                return false;
            if (nowUtc < failure.ExpiresUtc &&
                Vector3.DistanceSquared(probe.Source, failure.Source) <= ReuseDistance * ReuseDistance)
                return true;
            _failures.Remove(key);
            return false;
        }
    }

    public void RememberUnreachable(Probe probe, DateTime nowUtc)
    {
        var key = new Key(probe.BotDatabaseId, probe.BotObjectId, probe.RegionId,
            probe.ZoneId, probe.Boarding);
        lock (_lock)
        {
            if (_failures.Count >= MaximumEntries && !_failures.ContainsKey(key))
            {
                foreach (Key expired in _failures.Where(pair => pair.Value.ExpiresUtc <= nowUtc)
                             .Select(pair => pair.Key).ToArray())
                    _failures.Remove(expired);
                if (_failures.Count >= MaximumEntries)
                    _failures.Remove(_failures.MinBy(pair => pair.Value.ExpiresUtc).Key);
            }
            _failures[key] = new Failure(probe.Source, nowUtc + Lifetime);
        }
    }
}

/// <summary>
/// Scores the complete trip through the live stable network. A route is always
/// actual waypoint travel: this class only chooses which ticket to board first.
/// After each ride the bot replans, allowing any number of useful connections.
/// </summary>
public static class AutonomousStableRoutePlanner
{
    public const short StableSpeed = 1000;   // the routes' own speed, as players' horses (owner 2026-11-05)
    public const int BoardingArrivalRadius = 45;
    private const int MaximumPathPoints = 5000;
    private const int MaximumBoardingDistance = 500;
    private static readonly TimeSpan NetworkCacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly object NetworkCacheLock = new();
    private static readonly Dictionary<(ushort RegionId, eRealm Realm), CandidateCache> NetworkCache = new();
    private static readonly AutonomousStableBoardingFailureCache FirstBoardingFailures = new();
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousStableRoutePlanner));

    [ServerProperty("bots", "bot_stable_landing_safety",
        "Owner 2026-11-10: solo gamebots skip a horse whose landing has a pack of aggressive monsters 4 or more levels above them (from spawn data) when they only pass through (the Parthanan Farm horse for Hibernians below 15).", true)]
    public static bool LandingSafetyEnabled = true;

    /// <summary>Owner 2026-11-10: spawns within this distance of a landing count for its threat.</summary>
    public const int LandingThreatRadius = 2_500;
    /// <summary>The landing threat is the level of the N-th strongest aggressive spawn there (a pack, not one lone named).</summary>
    public const int LandingThreatPack = 3;
    /// <summary>A bot whose goal is this close to the landing is going there, not through it, and may ride.</summary>
    public const int LandingGoalRadius = 3_000;

    /// <summary>
    /// Pure rule (owner 2026-11-10, threshold fixed by the critic): a landing is unsafe for a solo bot passing through when
    /// its threat is MORE than 3 levels above the bot (threat 18: level 14 skips it, level 15 keeps the period horse).
    /// </summary>
    public static bool LandingUnsafe(int landingThreatLevel, int botLevel, double landingToGoal) =>
        landingThreatLevel > botLevel + AutonomousCampThreatPolicy.LevelsAboveSoloBot && landingToGoal > LandingGoalRadius;

    /// <summary>
    /// Pure rule: the level a spawn row threatens with, or 0 when it never attacks on sight (a realm NPC, aggro level 0,
    /// no aggro range, peaceful). A template that replaces mob values gives its highest level ("17-18" is 18) and its aggro.
    /// </summary>
    public static int SpawnThreatLevel(byte realm, int mobLevel, int mobAggroLevel, int mobAggroRange, uint mobFlags,
        bool templateReplaces, string templateLevel, int templateAggroLevel, int templateAggroRange)
    {
        if (realm != 0 || (mobFlags & (uint)GameNPC.eFlags.PEACE) != 0)
            return 0;
        int level = mobLevel, aggroLevel = mobAggroLevel, aggroRange = mobAggroRange;
        if (templateReplaces)
        {
            aggroLevel = templateAggroLevel;
            aggroRange = templateAggroRange;
            int highest = HighestNumber(templateLevel);
            if (highest > 0)
                level = highest;
        }
        return aggroLevel > 0 && aggroRange > 0 ? Math.Max(1, level) : 0;
    }

    /// <summary>Pure rule: the level of the <paramref name="pack"/>-th strongest of the spawn levels (0 with fewer).</summary>
    public static int LandingThreat(IEnumerable<int> spawnLevels, int pack)
    {
        int[] top = spawnLevels.Where(level => level > 0).OrderByDescending(level => level).Take(pack).ToArray();
        return top.Length < pack ? 0 : top[^1];
    }

    private static int HighestNumber(string text)
    {
        int highest = 0, current = -1;
        foreach (char c in (text ?? string.Empty) + " ")
        {
            if (char.IsDigit(c))
                current = Math.Min(10_000, (current < 0 ? 0 : current * 10) + (c - '0'));
            else if (current >= 0)
            {
                highest = Math.Max(highest, current);
                current = -1;
            }
        }
        return Math.Min(highest, 255);
    }

    private readonly record struct SpawnThreat(int X, int Y, int Level);
    // aggressive spawn points per region from the world database, read once (spawn rows do not move; live NPCs that are
    // dead or respawning must not make a landing flip between safe and unsafe, critic 2026-11-10). Owner 2026-11-10: read
    // on a pool thread, never inside NetworkCacheLock on a game-loop thread; a failed read is not kept and is retried.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ushort, SpawnThreat[]> RegionSpawnThreats = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ushort, long> SpawnThreatNextLoad = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(ushort Region, int X, int Y), int> LandingThreats = new();
    private const long SpawnThreatRetryMilliseconds = 60_000;

    /// <summary>The region's spawn threats once read; until then starts one background read and returns null.</summary>
    private static SpawnThreat[] SpawnThreatsFor(ushort region, long now)
    {
        if (RegionSpawnThreats.TryGetValue(region, out SpawnThreat[] threats))
            return threats;
        long next = SpawnThreatNextLoad.GetOrAdd(region, 0L);
        if (now >= next && SpawnThreatNextLoad.TryUpdate(region, long.MaxValue, next))
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                SpawnThreat[] loaded = LoadSpawnThreats(region);
                if (loaded != null)
                    RegionSpawnThreats[region] = loaded;
                else
                    SpawnThreatNextLoad[region] = GameLoop.GameLoopTime + SpawnThreatRetryMilliseconds;
            });
        return null;
    }

    /// <summary>Null when the read failed (no database, an exception), so it is retried instead of kept.</summary>
    private static SpawnThreat[] LoadSpawnThreats(ushort region)
    {
        try
        {
            if (GameServer.Database == null)
                return null;
            List<SpawnThreat> threats = new();
            foreach (DbMob mob in DOLDB<DbMob>.SelectObjects(DB.Column("Region").IsEqualTo(region)))
            {
                NpcTemplate template = mob.NPCTemplateID > 0 ? NpcTemplateMgr.GetTemplate(mob.NPCTemplateID) : null;
                int level = SpawnThreatLevel(mob.Realm, mob.Level, mob.AggroLevel, mob.AggroRange, mob.Flags,
                    template?.ReplaceMobValues == true, template?.Level, template?.AggroLevel ?? 0, template?.AggroRange ?? 0);
                if (level > 0)
                    threats.Add(new SpawnThreat(mob.X, mob.Y, level));
            }
            return threats.ToArray();
        }
        catch (Exception e)
        {
            Log.Warn($"AUTONOMOUS_STABLE_LANDING_THREAT_LOAD_FAILED region={region}: {e.Message}");
            return null;
        }
    }

    /// <summary>A landing's threat level, worked out once per landing; 0 while the region's spawn data is still being read.</summary>
    private static int LandingThreatLevel(ushort region, int x, int y, long now)
    {
        if (LandingThreats.TryGetValue((region, x, y), out int level))
            return level;
        SpawnThreat[] spawns = SpawnThreatsFor(region, now);
        if (spawns == null)
            return 0;
        const double radiusSquared = (double)LandingThreatRadius * LandingThreatRadius;
        level = LandingThreat(spawns
            .Where(s => (double)(s.X - x) * (s.X - x) + (double)(s.Y - y) * (s.Y - y) <= radiusSquared)
            .Select(s => s.Level), LandingThreatPack);
        if (LandingThreats.Count > 4_096)
            LandingThreats.Clear();
        LandingThreats[(region, x, y)] = level;
        return level;
    }

    // owner 2026-11-10: the AUTONOMOUS_STABLE_LANDING_SKIP line needs a second, unrestricted search; it runs at most once per
    // bot per few minutes, not on every FindBest
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> NextLandingSkipSample = new();
    public const long LandingSkipSampleMilliseconds = 5 * 60_000;

    /// <summary>True (and the next sample is put off) when this bot may run the unrestricted search for the skip log line.</summary>
    public static bool TryTakeLandingSkipSample(int botId, long now)
    {
        if (NextLandingSkipSample.TryGetValue(botId, out long next) && now < next)
            return false;
        if (NextLandingSkipSample.Count > 8_192)
            NextLandingSkipSample.Clear();
        NextLandingSkipSample[botId] = now + LandingSkipSampleMilliseconds;
        return true;
    }

    public readonly record struct LegMetric(
        int Index,
        double OriginX,
        double OriginY,
        double EndX,
        double EndY,
        double RideSeconds,
        long Price);

    public readonly record struct RouteDecision(
        int FirstLegIndex,
        double EstimatedSeconds,
        double DirectWalkSeconds,
        int HopCount,
        long PlannedPrice);

    public sealed record Choice(
        GameStableMaster Master,
        DbItemTemplate Ticket,
        PathPoint Route,
        string DestinationName,
        double RideSeconds,
        double EstimatedSeconds,
        double DirectWalkSeconds,
        int PlannedHops,
        long PlannedPrice,
        Vector3 BoardingPoint,
        Vector3 InteractionPoint);

    private sealed record Candidate(
        GameStableMaster Master,
        DbItemTemplate Ticket,
        PathPoint Route,
        PathPoint End,
        double RideSeconds,
        Vector3 BoardingPoint,
        Vector3 InteractionPoint);

    private sealed record CandidateCache(DateTime ExpiresUtc, Candidate[] Candidates);

    /// <summary>
    /// Dijkstra over route endpoints. Walking connects the bot, every ticket
    /// origin, every ticket endpoint, and the goal. Positive edge costs prevent
    /// cycling, while the first selected leg is returned for live execution.
    /// </summary>
    public static RouteDecision? ChooseFirstLeg(
        double startX,
        double startY,
        double goalX,
        double goalY,
        double walkSpeed,
        long availableMoney,
        IReadOnlyList<LegMetric> legs,
        IReadOnlySet<int> excludedBoardingLegs = null,
        IReadOnlySet<int> unusableLegs = null)   // owner 2026-11-10: never ridden, first or later (unsafe landings)
    {
        walkSpeed = Math.Max(1d, walkSpeed);
        double direct = Distance(startX, startY, goalX, goalY) / walkSpeed;
        if (legs == null || legs.Count == 0)
            return null;

        int count = legs.Count;
        double[] best = Enumerable.Repeat(double.PositiveInfinity, count).ToArray();
        int[] first = Enumerable.Repeat(-1, count).ToArray();
        int[] hops = new int[count];
        long[] spent = new long[count];
        bool[] visited = new bool[count];

        for (int index = 0; index < count; index++)
        {
            LegMetric leg = legs[index];
            if (excludedBoardingLegs?.Contains(index) == true || unusableLegs?.Contains(index) == true)
                continue;
            if (leg.Price < 0 || leg.Price > availableMoney || leg.RideSeconds <= 0)
                continue;
            best[index] = Distance(startX, startY, leg.OriginX, leg.OriginY) / walkSpeed + leg.RideSeconds;
            first[index] = index;
            hops[index] = 1;
            spent[index] = leg.Price;
        }

        double bestTrip = direct;
        int bestFirst = -1;
        int bestHops = 0;
        long bestPrice = 0;

        for (int iteration = 0; iteration < count; iteration++)
        {
            int currentIndex = -1;
            double currentCost = double.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                if (!visited[index] && best[index] < currentCost)
                {
                    currentIndex = index;
                    currentCost = best[index];
                }
            }

            if (currentIndex < 0)
                break;
            visited[currentIndex] = true;
            LegMetric current = legs[currentIndex];

            double completed = currentCost + Distance(current.EndX, current.EndY, goalX, goalY) / walkSpeed;
            if (completed + 0.25 < bestTrip)
            {
                bestTrip = completed;
                bestFirst = first[currentIndex];
                bestHops = hops[currentIndex];
                bestPrice = spent[currentIndex];
            }

            for (int nextIndex = 0; nextIndex < count; nextIndex++)
            {
                if (visited[nextIndex] || unusableLegs?.Contains(nextIndex) == true)
                    continue;
                LegMetric next = legs[nextIndex];
                if (next.Price < 0 || spent[currentIndex] > availableMoney - next.Price || next.RideSeconds <= 0)
                    continue;

                double candidate = currentCost +
                                   Distance(current.EndX, current.EndY, next.OriginX, next.OriginY) / walkSpeed +
                                   next.RideSeconds;
                if (candidate + 0.001 >= best[nextIndex])
                    continue;

                best[nextIndex] = candidate;
                first[nextIndex] = first[currentIndex];
                hops[nextIndex] = hops[currentIndex] + 1;
                spent[nextIndex] = spent[currentIndex] + next.Price;
            }
        }

        return bestFirst < 0
            ? null
            : new RouteDecision(bestFirst, bestTrip, direct, bestHops, bestPrice);
    }

    public static Choice FindBest(GameBot bot, Vector3 goal, IReadOnlySet<GameStableMaster> excludedBoardingMasters = null,
        bool boundedMeetupApproach = false)
    {
        if (bot?.CurrentRegion == null)
            return null;

        List<Candidate> candidates = GetCandidates(bot);
        if (candidates.Count == 0)
            return null;

        LegMetric[] metrics = candidates.Select((candidate, index) => new LegMetric(
            index,
            candidate.BoardingPoint.X,
            candidate.BoardingPoint.Y,
            candidate.End.X,
            candidate.End.Y,
            candidate.RideSeconds,
            Math.Max(0L, (long)candidate.Ticket.Price))).ToArray();

        long money = bot.DatabaseID > 0 ? AutonomousBotEconomy.GetMoney(bot.DatabaseID) : 0;
        // Exclude only boarding here. A later horse can still reach that master
        // without repeating this bot's failed walk from its current location.
        HashSet<int> excluded = excludedBoardingMasters == null ? null : candidates
            .Select((candidate, index) => (candidate, index))
            .Where(entry => excludedBoardingMasters.Contains(entry.candidate.Master))
            .Select(entry => entry.index).ToHashSet();

        // A timed meetup must not spend most of its fifteen-minute attendance
        // window walking away to a distant first horse (the Vuloch failures).
        // Later network legs remain legal; outside assembly behavior is unchanged.
        if (boundedMeetupApproach)
            for (int i = 0; i < candidates.Count; i++)
                if (!CanApproachStableDuringMeetup(Distance(bot.X, bot.Y,
                        candidates[i].BoardingPoint.X, candidates[i].BoardingPoint.Y), bot.MaxSpeed))
                    (excluded ??= []).Add(i);

        // Owner 2026-11-10 (P3): a solo bot never rides to a landing it cannot survive on its way elsewhere (the Parthanan
        // Farm horse lands 1.7 km from level 17-18 parthanans: 24% of riders died within 5 minutes, levels 9-14), nor to
        // one beside a live death hotspot, and does not walk to a horse straight through a hotspot (P2).
        HashSet<int> unusable = null;
        string skipped = null;
        long nowTick = GameLoop.GameLoopTime;
        if (bot.Group == null && !boundedMeetupApproach)
        {
            AutonomousRoadHotspots.Hotspot[] hotspots = AutonomousRoadHotspots.PathCheckEnabled
                ? AutonomousRoadHotspots.DangerousTo(bot.Level, nowTick)
                : Array.Empty<AutonomousRoadHotspots.Hotspot>();
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                // owner 2026-11-10: the switch applies here, at once (the threat is no longer baked into the 5-minute cache)
                int landingThreat = LandingSafetyEnabled
                    ? LandingThreatLevel(bot.CurrentRegionID, candidate.End.X, candidate.End.Y, nowTick)
                    : 0;
                bool threat = LandingSafetyEnabled && LandingUnsafe(landingThreat, bot.Level,
                    Distance(candidate.End.X, candidate.End.Y, goal.X, goal.Y));
                if (threat || hotspots.Length > 0 &&
                    AutonomousRoadHotspots.LandingNearHotspot(bot.CurrentRegionID, candidate.End.X, candidate.End.Y, hotspots))
                {
                    (unusable ??= []).Add(i);
                    skipped = (skipped == null ? string.Empty : skipped + ",") + candidate.Ticket.Id_nb +
                              (threat ? $"(threat {landingThreat})" : "(hotspot)");
                }
                if (hotspots.Length > 0 && AutonomousRoadHotspots.WalkLegUnsafe(bot.CurrentRegionID, (int)bot.X, (int)bot.Y,
                        (int)candidate.BoardingPoint.X, (int)candidate.BoardingPoint.Y, hotspots))
                    (excluded ??= []).Add(i);
            }
        }

        // A straight-line cost can choose a remote master across a broken
        // zone seam. Reuse recent failures only while this bot remains in the
        // same small pocket, then prove at most two new first walks per search.
        // The next search advances to other candidates without repeating those
        // costly probes. Later network legs remain available.
        Vector3 source = new(bot.X, bot.Y, bot.Z);
        DateTime nowUtc = DateTime.UtcNow;
        AutonomousStableBoardingFailureCache.Probe ProbeFor(Candidate candidate) => new(
            bot.DatabaseID, bot.ObjectID, bot.CurrentRegionID, bot.CurrentZone?.ID ?? 0,
            candidate.BoardingPoint, source);
        for (int index = 0; index < candidates.Count; index++)
            if (FirstBoardingFailures.WasRecentlyUnreachable(ProbeFor(candidates[index]), nowUtc))
                (excluded ??= []).Add(index);

        // owner 2026-11-10: one line when the landing rule changed the plan (the ride it would have taken otherwise)
        RouteDecision? unrestricted = unusable == null || !TryTakeLandingSkipSample(bot.ObjectID, nowTick) ? null : ChooseFirstLeg(
            bot.X, bot.Y, goal.X, goal.Y, Math.Max(1, (int)bot.MaxSpeed), money, metrics, excluded);
        void LogLandingSkip(RouteDecision? chosen)
        {
            if (!unrestricted.HasValue || chosen.HasValue && chosen.Value.FirstLegIndex == unrestricted.Value.FirstLegIndex &&
                Math.Abs(chosen.Value.EstimatedSeconds - unrestricted.Value.EstimatedSeconds) < 0.5)
                return;
            Log.Info($"AUTONOMOUS_STABLE_LANDING_SKIP bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} " +
                     $"region={bot.CurrentRegionID} skipped=\"{skipped}\" " +
                     $"wouldBoard=\"{candidates[unrestricted.Value.FirstLegIndex].Ticket.Id_nb}\" " +
                     $"now=\"{(chosen.HasValue ? candidates[chosen.Value.FirstLegIndex].Ticket.Id_nb : "walk")}\" " +
                     $"seconds={unrestricted.Value.EstimatedSeconds:0}->{(chosen.HasValue ? chosen.Value.EstimatedSeconds : unrestricted.Value.DirectWalkSeconds):0}");
        }

        for (int attempt = 0; attempt < Math.Min(AutonomousStableBoardingFailureCache.MaximumNewProbesPerSearch,
                 candidates.Count); attempt++)
        {
            RouteDecision? decision = ChooseFirstLeg(
                bot.X, bot.Y, goal.X, goal.Y, Math.Max(1, (int)bot.MaxSpeed), money, metrics, excluded, unusable);
            if (attempt == 0)
                LogLandingSkip(decision);
            if (!decision.HasValue)
                return null;

            Candidate selected = candidates[decision.Value.FirstLegIndex];
            if (!CanWalkToFirstBoardingLeg(bot, selected.BoardingPoint))
            {
                FirstBoardingFailures.RememberUnreachable(ProbeFor(selected), nowUtc);
                (excluded ??= []).Add(decision.Value.FirstLegIndex);
                continue;
            }
            return new Choice(
                selected.Master,
                selected.Ticket,
                CloneRoute(selected.Route),
                TicketDestination(selected.Ticket),
                selected.RideSeconds,
                decision.Value.EstimatedSeconds,
                decision.Value.DirectWalkSeconds,
                decision.Value.HopCount,
                decision.Value.PlannedPrice,
                selected.BoardingPoint,
                selected.InteractionPoint);
        }
        return null;
    }

    /// <summary>
    /// Use a real ticket whose landing can walk to an authoritative portal's
    /// activation radius. A straight-line walk estimate is not a valid fallback
    /// when the outdoor zone itinerary reverses across a disconnected seam.
    /// </summary>
    public static Choice FindDirectRideToConnectedPortal(GameBot bot, Vector3 portal, int arrivalRadius,
        IReadOnlySet<GameStableMaster> excludedBoardingMasters = null)
    {
        if (bot?.CurrentRegion == null || arrivalRadius <= 0)
            return null;

        IPathfindingMgr nav = PathfindingProvider.Instance;
        Zone portalZone = bot.CurrentRegion.GetZone((int)portal.X, (int)portal.Y);
        if (portalZone == null || !nav.IsAvailable || !nav.HasNavmesh(portalZone))
            return null;

        long money = bot.DatabaseID > 0 ? AutonomousBotEconomy.GetMoney(bot.DatabaseID) : 0;
        double speed = Math.Max(1, (int)bot.MaxSpeed);
        Vector3 source = new(bot.X, bot.Y, bot.Z);
        DateTime nowUtc = DateTime.UtcNow;
        var eligible = new List<(Candidate Candidate, double Seconds)>();
        foreach (Candidate candidate in GetCandidates(bot))
        {
            long price = Math.Max(0L, (long)candidate.Ticket.Price);
            if (price > money || excludedBoardingMasters?.Contains(candidate.Master) == true ||
                bot.CurrentRegion.GetZone(candidate.End.X, candidate.End.Y) != portalZone)
                continue;

            Vector3 landing = new(candidate.End.X, candidate.End.Y, candidate.End.Z);
            if (!AutonomousZonePointApproach.TryResolve(nav, portalZone, landing, portal,
                    arrivalRadius, out Vector3 approach))
                continue;

            double seconds = Distance(bot.X, bot.Y, candidate.BoardingPoint.X, candidate.BoardingPoint.Y) / speed +
                             candidate.RideSeconds +
                             Distance(landing.X, landing.Y, approach.X, approach.Y) / speed;
            eligible.Add((candidate, seconds));
        }

        foreach ((Candidate candidate, double seconds) in eligible.OrderBy(entry => entry.Seconds))
        {
            var probe = new AutonomousStableBoardingFailureCache.Probe(bot.DatabaseID, bot.ObjectID,
                bot.CurrentRegionID, bot.CurrentZone?.ID ?? 0, candidate.BoardingPoint, source);
            if (FirstBoardingFailures.WasRecentlyUnreachable(probe, nowUtc))
                continue;
            if (!CanWalkToFirstBoardingLeg(bot, candidate.BoardingPoint))
            {
                FirstBoardingFailures.RememberUnreachable(probe, nowUtc);
                continue;
            }

            return new Choice(candidate.Master, candidate.Ticket, CloneRoute(candidate.Route),
                TicketDestination(candidate.Ticket), candidate.RideSeconds, seconds,
                Distance(bot.X, bot.Y, portal.X, portal.Y) / speed, 1,
                Math.Max(0L, (long)candidate.Ticket.Price), candidate.BoardingPoint, candidate.InteractionPoint);
        }
        return null;
    }

    /// <summary>
    /// The answer depends on the realm, where the bot stands and the boarding point only, so bots standing together
    /// share it for a while. Each proof is a chain of zone steps and a corridor (~300 ms); in run 17 (2026-10-17) it
    /// was the second-largest cost of slow AI turns (38 s over 124 turns in 48 minutes), mostly crowds in towns.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(eRealm, ushort, int, int, int, int, int), (bool Reachable, long Until)>
        BoardingWalks = new();
    public const long BoardingWalkMemoryMilliseconds = 120_000;
    public const long BoardingWalkFailureMemoryMilliseconds = 60_000;

    private static bool CanWalkToFirstBoardingLeg(GameBot bot, Vector3 boarding)
    {
        var key = (bot.Realm, bot.CurrentRegionID, (int)bot.X >> 8, (int)bot.Y >> 8, (int)bot.Z >> 8,
            (int)boarding.X >> 5, (int)boarding.Y >> 5);
        long now = GameLoop.GameLoopTime;
        if (BoardingWalks.TryGetValue(key, out var known) && now < known.Until)
            return known.Reachable;
        bool reachable = ProveWalkToFirstBoardingLeg(bot, boarding);
        if (BoardingWalks.Count > 20_000) BoardingWalks.Clear();
        BoardingWalks[key] = (reachable, now + (reachable ? BoardingWalkMemoryMilliseconds : BoardingWalkFailureMemoryMilliseconds));
        return reachable;
    }

    private static bool ProveWalkToFirstBoardingLeg(GameBot bot, Vector3 boarding)
    {
        Region region = bot.CurrentRegion;
        Zone currentZone = bot.CurrentZone;
        Zone boardingZone = region?.GetZone((int)boarding.X, (int)boarding.Y);
        IPathfindingMgr nav = PathfindingProvider.Instance;
        if (currentZone == null || boardingZone == null || !nav.IsAvailable ||
            !AutonomousNavigationSurface.TryFloor(nav, currentZone,
                new(bot.X, bot.Y, bot.Z), out Vector3 cursor))
            return false;
        var visited = new HashSet<Zone>();
        for (int hop = 0; hop < 16 && currentZone != boardingZone; hop++)
        {
            if (!visited.Add(currentZone) ||
                !AutonomousZoneItinerary.TryNextStep(region, currentZone, boardingZone,
                    cursor, boarding, nav, out AutonomousZoneBoundaryRouting.Step step,
                    zone => AutonomousRealmBoundary.Allows(bot.Realm, bot.CurrentRegionID, zone.ID)))
                return false;
            cursor = step.Outside;
            currentZone = region.GetZone((int)cursor.X, (int)cursor.Y);
            if (currentZone == null)
                return false;
        }
        return currentZone == boardingZone &&
            AutonomousZoneItinerary.HasCompleteCorridor(nav, currentZone, cursor, boarding);
    }

    public static bool CanUseAsFirstBoardingLeg(bool sameZone, bool completeCorridor) =>
        !sameZone || completeCorridor;

    public static bool CanApproachStableDuringMeetup(double distance, double speed) =>
        double.IsFinite(distance) && distance >= 0 && speed > 0 && distance <= speed * 120;

    public static bool FinishMeetupOnFoot(bool meetup, float distanceSquared) =>
        meetup && float.IsFinite(distanceSquared) && distanceSquared >= 0 && distanceSquared <= 2_000 * 2_000;

    public static bool MeetupBoardingExpired(bool meetup, long started, long now, long lastProgress = 0) =>
        meetup && now >= started && now - started >= 120_000 &&
        (now - started >= 300_000 || lastProgress <= started || now - lastProgress >= 90_000);

    public static bool MayPlanMeetupHorse(bool meetup, string groupId, string failedGroupId) =>
        !meetup || string.IsNullOrEmpty(groupId) ||
        !string.Equals(groupId, failedGroupId, StringComparison.Ordinal);

    /// <summary>The old Nalliten and Svasud Faste tickets end below this Mularn street.
    /// Match the measured endpoint, not every rider arriving in the town.</summary>
    public static bool IsAuditedMularnLanding(ushort regionId, Vector3 position) =>
        regionId == 100 && position.Z is >= 4_660 and <= 4_705 &&
        Vector2.DistanceSquared(new(position.X, position.Y), new(803743, 722129)) <= 80 * 80;

    /// <summary>
    /// The walkable floor under a stable-route landing that is not on the navmesh: the nearest floor
    /// in height at the same spot (at most 48 units across, 600 up or down). False when the landing
    /// is already on the floor or no floor is close enough.
    /// </summary>
    public static bool TryGroundLanding(IPathfindingMgr nav, Zone zone, Vector3 landing, out Vector3 floor)
    {
        floor = default;
        if (nav == null || zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
        if (nav.GetClosestPoint(zone, landing, 24, 24, 64, nav.DefaultFilters).HasValue) return false;
        Vector3? ground = nav.GetClosestPoint(zone, landing, 48, 48, 600, nav.DefaultFilters);
        if (!ground.HasValue || !IsGroundLanding(landing, ground.Value)) return false;
        floor = ground.Value;
        return true;
    }

    public static bool IsGroundLanding(Vector3 landing, Vector3 ground) =>
        Vector2.Distance(new(landing.X, landing.Y), new(ground.X, ground.Y)) <= 48 && MathF.Abs(ground.Z - landing.Z) <= 600;

    public static bool ShouldCorrectAuditedMularnLanding(bool confirmedArrival, bool alive,
        ushort regionId, Vector3 position) =>
        confirmedArrival && alive && IsAuditedMularnLanding(regionId, position);

    // NpcMovementComponent toggles FiredFlag while traversing even a Once
    // route. Cached topology must therefore be cloned per rider or one horse
    // can make another skip/reverse a point and appear riderless.
    internal static PathPoint CloneRoute(PathPoint route)
    {
        PathPoint first = null;
        PathPoint previous = null;
        for (PathPoint current = route; current != null; current = current.Next)
        {
            var copy = new PathPoint(current.X, current.Y, current.Z, current.MaxSpeed, current.Type)
            {
                WaitTime = current.WaitTime,
            };
            first ??= copy;
            copy.Prev = previous;
            if (previous != null)
                previous.Next = copy;
            previous = copy;
        }
        return first;
    }

    private static List<Candidate> GetCandidates(GameBot bot)
    {
        var key = (bot.CurrentRegionID, bot.Realm);
        DateTime now = DateTime.UtcNow;
        CandidateCache cache;
        lock (NetworkCacheLock)
        {
            if (!NetworkCache.TryGetValue(key, out cache) || cache.ExpiresUtc <= now)
            {
                cache = new CandidateCache(now + NetworkCacheLifetime,
                    BuildCandidates(bot.CurrentRegion, bot.Realm).ToArray());
                NetworkCache[key] = cache;
            }
        }

        // Masters can be removed between cache rebuilds; live state validation
        // is cheap and prevents choosing a stale boarding point.
        return cache.Candidates
            .Where(candidate => candidate.Master?.ObjectState is GameObject.eObjectState.Active &&
                                candidate.Master.CurrentRegion == bot.CurrentRegion)
            .ToList();
    }

    private static List<Candidate> BuildCandidates(Region region, eRealm realm)
    {
        List<Candidate> candidates = new();
        foreach (GameStableMaster master in region.Objects.OfType<GameStableMaster>()
                     .Where(master => master.ObjectState is GameObject.eObjectState.Active &&
                                      (master.Realm == realm || master.Realm == eRealm.None)))
        {
            if (master.TradeItems == null)
                continue;

            foreach (DictionaryEntry entry in master.TradeItems.GetAllItems())
            {
                if (entry.Value is not DbItemTemplate ticket || ticket.Item_Type != 40)
                    continue;
                PathPoint route = MovementMgr.LoadPath(ticket.Id_nb);
                if (!TryMeasureRoute(master, route, out PathPoint endpoint, out double rideSeconds))
                    continue;
                if (region.GetZone(endpoint.X, endpoint.Y) == null)
                    continue;
                // Some old ticket origins are below the installed terrain.
                // Normalize only the walking approach, never the authoritative
                // horse waypoints.
                Zone boardingZone = region.GetZone(route.X, route.Y);
                IPathfindingMgr nav = PathfindingProvider.Instance;
                if (boardingZone == null || !TryResolveBoardingPoint(new(route.X, route.Y, route.Z),
                        p => nav.GetClosestPoint(boardingZone, p, 48, 48, 256, nav.DefaultFilters), out Vector3 boarding))
                    continue;
                // Match proximity on the same walkable surface when this old
                // NPC also has stale terrain Z. No entity or ticket is moved.
                if (!TryResolveBoardingPoint(new(master.X, master.Y, master.Z),
                        p => nav.GetClosestPoint(boardingZone, p, 48, 48, 256, nav.DefaultFilters), out Vector3 interaction) ||
                    region.GetZone((int)boarding.X, (int)boarding.Y) != boardingZone ||
                    region.GetZone((int)interaction.X, (int)interaction.Y) != boardingZone ||
                    !TryChooseBoardingApproach(boarding, interaction, master.InteractDistance,
                        point => nav.GetClosestPoint(boardingZone, point, 16, 16, 64, nav.DefaultFilters),
                        out Vector3 safeBoarding) ||
                    region.GetZone((int)safeBoarding.X, (int)safeBoarding.Y) != boardingZone ||
                    !AutonomousZoneItinerary.HasCompleteCorridor(nav, boardingZone, safeBoarding, interaction))
                    continue;
                // owner 2026-11-10: the landing's threat is worked out in FindBest, outside this lock (solo landing rule)
                candidates.Add(new Candidate(master, ticket, route, endpoint, rideSeconds, safeBoarding, interaction));
            }
        }
        return candidates;
    }

    public static bool TryResolveBoardingPoint(Vector3 point, Func<Vector3, Vector3?> snap, out Vector3 result)
    {
        result = default;
        Vector3? snapped = snap(point);
        if (!snapped.HasValue || !float.IsFinite(snapped.Value.X) || !float.IsFinite(snapped.Value.Y) ||
            !float.IsFinite(snapped.Value.Z) || Math.Abs(snapped.Value.Z - point.Z) > 256 ||
            Vector2.Distance(new(point.X, point.Y), new(snapped.Value.X, snapped.Value.Y)) > 48)
            return false;
        result = snapped.Value;
        return true;
    }

    /// <summary>Choose a walkable boarding position inside both the horse's
    /// start radius and the master's real interaction radius. The normal
    /// 45-unit arrival tolerance must not strand a bot just outside the
    /// master's range, as observed at Pheuloc's Howth ticket.</summary>
    public static bool TryChooseBoardingApproach(Vector3 routeOrigin, Vector3 interaction,
        float interactionRadius, Func<Vector3, Vector3?> snap, out Vector3 approach)
    {
        approach = default;
        if (!float.IsFinite(interactionRadius) || interactionRadius < BoardingArrivalRadius ||
            snap == null)
            return false;

        Vector3 delta = interaction - routeOrigin;
        float separation = delta.Length();
        if (!float.IsFinite(separation))
            return false;
        float move = Math.Min(44f, Math.Max(0f, separation - (interactionRadius - BoardingArrivalRadius - 1)));
        Vector3 desired = separation > 0 ? routeOrigin + delta / separation * move : routeOrigin;
        Vector3? candidate = snap(desired);
        if (candidate.HasValue && float.IsFinite(candidate.Value.X) &&
            float.IsFinite(candidate.Value.Y) && float.IsFinite(candidate.Value.Z) &&
            Vector3.Distance(candidate.Value, routeOrigin) <= BoardingArrivalRadius &&
            Vector3.Distance(candidate.Value, interaction) <= interactionRadius - BoardingArrivalRadius)
        {
            approach = candidate.Value;
            return true;
        }

        // Some horse/gryphon paths start further from their master than any spot that is
        // both at the path start and in the master's range (Korlis's Hagall -> Iarn Dwarf
        // Camp gryphon starts 229 units away; range is 192), so that leg was never offered
        // and bots swam to the next gryphon instead. The bot's mount spawns at the path
        // start and carries the rider there, as it does for a player, so wait in the
        // master's range at the point nearest the path start instead.
        if (separation <= 0 || separation > MaximumBoardingDistance)
            return false;
        float reach = interactionRadius - BoardingArrivalRadius - 1;
        Vector3 inRange = interaction + delta / separation * -Math.Min(reach, separation);
        Vector3? fallback = snap(inRange);
        if (!fallback.HasValue || !float.IsFinite(fallback.Value.X) ||
            !float.IsFinite(fallback.Value.Y) || !float.IsFinite(fallback.Value.Z) ||
            Vector3.Distance(fallback.Value, interaction) > interactionRadius - BoardingArrivalRadius)
            return false;
        approach = fallback.Value;
        return true;
    }

    private static bool TryMeasureRoute(
        GameStableMaster master,
        PathPoint route,
        out PathPoint endpoint,
        out double rideSeconds)
    {
        endpoint = route;
        rideSeconds = 0;
        if (route == null || route.Type is not EPathType.Once ||
            Distance(master.X, master.Y, route.X, route.Y) > MaximumBoardingDistance)
            return false;

        return TryMeasureRide(route, out endpoint, out rideSeconds);
    }

    /// <summary>
    /// Measures a validated stable path without choosing, buying, or charging
    /// for a ticket.  Player-led temporary companions use this only to mirror
    /// the real player's already-approved native ride.
    /// </summary>
    public static bool TryMeasureRide(PathPoint route, out PathPoint endpoint, out double rideSeconds)
    {
        endpoint = route;
        rideSeconds = 0;
        if (route == null || route.Type is not EPathType.Once)
            return false;

        int points = 0;
        while (endpoint.Next != null && points++ < MaximumPathPoints)
        {
            PathPoint next = endpoint.Next;
            short segmentSpeed = (short)Math.Clamp((int)next.MaxSpeed, 1, (int)StableSpeed);
            rideSeconds += Distance(endpoint.X, endpoint.Y, next.X, next.Y) / segmentSpeed;
            rideSeconds += Math.Max(0, endpoint.WaitTime) * 0.1;
            endpoint = next;
        }

        return endpoint.Next == null && points > 0 && rideSeconds > 0;
    }

    private static string TicketDestination(DbItemTemplate ticket)
    {
        string name = ticket?.Name?.Trim() ?? string.Empty;
        int marker = name.IndexOf("ticket to", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
            name = name[(marker + "ticket to".Length)..].Trim();
        return string.IsNullOrWhiteSpace(name) ? "the next stable" : name;
    }

    private static double Distance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
