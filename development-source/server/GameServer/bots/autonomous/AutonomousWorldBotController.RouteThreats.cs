using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    public sealed partial class AutonomousWorldBotController
    {
        private long _nextRouteThreatScanTick;
        private long _lastRouteThreatRetargetTick = long.MinValue / 2;
        private readonly Dictionary<GameNPC, int> _routeThreatDetours = new();
        // Monsters next to the route that cannot be walked to (a ledge above the road): they
        // cannot reach the route either, so they are passed by instead of pulled. Run 8: route
        // pulls on such monsters left bots standing until the 15-minute watchdog.
        private readonly HashSet<GameNPC> _unreachableRouteThreats = new();
        // The last route pull: the same monster found again, still out of combat, means the pull
        // never landed. After three it is passed by like an unreachable one.
        private GameNPC _lastRouteThreatPull;
        private int _routeThreatPullRepeats;
        private long _lastRouteThreatPullTick;
        private const int RouteThreatPullAttempts = 3;
        // A party holding in front of a route monster it may not pull yet. Run 17 (2026-10-17): 12,472 of 13,547 level 50
        // party route-threat decisions were the same hold repeated (114 loops in 78 parties, one for 98 minutes): only
        // the leader looks ahead, only the party's puller may open a pull, and a leader that was not the puller held
        // forever. Now the puller takes the pull, and a hold ends after RouteThreatHoldLimitMilliseconds.
        private GameNPC _routeThreatHold;
        private long _routeThreatHoldSince;
        // owner 2026-11-08: where THIS bot died on the road in the last few minutes (per bot, never shared, a few spots)
        private readonly List<(ushort Region, int X, int Y, long Until)> _roadDeathSpots = new(4);
        // owner 2026-11-10: the last hotspot replans (path check), at most AutonomousRoadHotspots.ReplanBudget per 15 minutes
        private readonly long[] _hotspotReplanTicks = new long[AutonomousRoadHotspots.ReplanBudget];
        private long _nextStableHotspotCheckTick;
        private long _nextServiceHotspotCheckTick;

        /// <summary>Owner 2026-11-10: spends one hotspot replan if the budget allows it.</summary>
        private bool TrySpendHotspotReplan(long now)
        {
            if (!AutonomousRoadHotspots.ReplanAllowed(_hotspotReplanTicks, now))
                return false;
            int oldest = 0;
            for (int i = 1; i < _hotspotReplanTicks.Length; i++)
                if (_hotspotReplanTicks[i] < _hotspotReplanTicks[oldest])
                    oldest = i;
            _hotspotReplanTicks[oldest] = now;
            return true;
        }

        /// <summary>
        /// Owner 2026-11-10 (P2): the live death hotspot a solo bot's real path (navmesh corners) passes, if any. The
        /// straight-line test in AvoidNoGo misses paths that bend through one (the Silvermine road). Null when none, when
        /// the check is off, or when the bot already used its replan budget (it then walks on as before).
        /// </summary>
        private AutonomousRoadHotspots.Hotspot HotspotOnPath(GameBot bot, ReadOnlySpan<Vector3> path, long now, out int leg)
        {
            leg = -1;
            if (!AutonomousRoadHotspots.PathCheckEnabled)
                return null;
            AutonomousRoadHotspots.Hotspot[] hotspots = AutonomousRoadHotspots.DangerousTo(bot.Level, now);
            if (hotspots.Length == 0)
                return null;
            leg = AutonomousRoadHotspots.FirstBlockedLeg(path, bot.CurrentRegionID, hotspots, out AutonomousRoadHotspots.Hotspot hit);
            if (leg < 0 || !TrySpendHotspotReplan(now))
                return null;
            return hit;
        }

        /// <summary>
        /// Owner 2026-11-10 (P2): before a solo bot walks to a horse, its real walk there is checked against live death
        /// hotspots (every 5 s); a horse whose walk passes one is skipped for <see cref="AutonomousRoadHotspots.HotspotSkipMilliseconds"/>.
        /// True when the pending horse was dropped.
        /// </summary>
        private bool SkipHorseWalkThroughHotspot(GameBot bot, AutonomousStableRoutePlanner.Choice stable)
        {
            long now = GameLoop.GameLoopTime;
            if (!AutonomousRoadHotspots.PathCheckEnabled || bot.Group != null || _groupDirective != null ||
                now < _nextStableHotspotCheckTick)
                return false;
            _nextStableHotspotCheckTick = now + 5_000;
            Span<Vector3> route = stackalloc Vector3[257];
            route[0] = new(bot.X, bot.Y, bot.Z);
            int count = bot.movementComponent.CopyUpcomingPath(stable.BoardingPoint, route[1..]) + 1;
            if (count < 2)
            {
                route[1] = stable.BoardingPoint;
                count = 2;
            }
            AutonomousRoadHotspots.Hotspot hit = HotspotOnPath(bot, route[..count], now, out int leg);
            if (hit == null)
                return false;
            GameStableMaster master = stable.Master;
            Log.Info($"AUTONOMOUS_STABLE_HOTSPOT_SKIP bot={bot.Name} id={bot.DatabaseID} level={bot.Level} " +
                     $"master=\"{master.Name}\" ticket=\"{stable.Ticket.Name}\" hotspot={hit.Region}:{hit.X},{hit.Y} " +
                     $"killerLevel={hit.KillerLevel} leg={leg} minutes={AutonomousRoadHotspots.HotspotSkipMilliseconds / 60_000}");
            // owner 2026-11-10: dropped without RejectPendingBoarding's AUTONOMOUS_STABLE_APPROACH_FAILED, so that count stays apart
            _failedBoardingMasters[master] = now + AutonomousRoadHotspots.HotspotSkipMilliseconds;
            _pendingStableChoice = null;
            _nextStableCheckTick = 0;
            return true;
        }

        /// <summary>
        /// Owner 2026-11-10 (P2c): a solo bot's real walk to a merchant, healer or trainer is checked against live death
        /// hotspots (every 5 s, same replan budget); a service whose walk passes one is skipped for this bot (as after a
        /// death on the way) and another is chosen. True when the service was dropped.
        /// </summary>
        private bool SkipServiceWalkThroughHotspot(GameBot bot, Vector3 point)
        {
            long now = GameLoop.GameLoopTime;
            if (!AutonomousRoadHotspots.ServiceWalkCheckEnabled || !AutonomousRoadHotspots.PathCheckEnabled ||
                _serviceNpc == null || bot.Group != null || _groupDirective != null || now < _nextServiceHotspotCheckTick)
                return false;
            _nextServiceHotspotCheckTick = now + 5_000;
            Span<Vector3> route = stackalloc Vector3[257];
            route[0] = new(bot.X, bot.Y, bot.Z);
            int count = bot.movementComponent.CopyUpcomingPath(point, route[1..]) + 1;
            if (count < 2)
            {
                // owner 2026-11-10 (review): no path issued yet. A straight line only stands in for a walk inside the
                // bot's own zone; a walk to another zone is judged on its real path a moment later, once it exists.
                if (bot.CurrentRegion?.GetZone((int)point.X, (int)point.Y) != bot.CurrentZone)
                {
                    _nextServiceHotspotCheckTick = now + 1_500;
                    return false;
                }
                route[1] = point;
                count = 2;
            }
            AutonomousRoadHotspots.Hotspot hit = HotspotOnPath(bot, route[..count], now, out int leg);
            if (hit == null)
                return false;
            string service = AutonomousServiceReachability.Key(_serviceNpc);
            Log.Info($"AUTONOMOUS_SERVICE_HOTSPOT_SKIP bot={bot.Name} id={bot.DatabaseID} level={bot.Level} " +
                     $"service=\"{_serviceNpc.Name}\" hotspot={hit.Region}:{hit.X},{hit.Y} killerLevel={hit.KillerLevel} leg={leg}");
            AutonomousServiceReachability.SkipForBot(service, bot.DatabaseID, now);
            _serviceNpc = null;
            _nextServiceSearchTick = 0;
            return true;
        }

        private void RememberRoadDeath(ushort region, int x, int y, long now)
        {
            _roadDeathSpots.RemoveAll(spot => spot.Until <= now);
            if (_roadDeathSpots.Count >= 4)
                _roadDeathSpots.RemoveAt(0);
            _roadDeathSpots.Add((region, x, y, now + AutonomousRouteThreatPolicy.RoadDeathMemoryMilliseconds));
        }

        private bool NearOwnRecentRoadDeath(ushort region, int x, int y, long now)
        {
            foreach (var spot in _roadDeathSpots)
                if (spot.Until > now && spot.Region == region &&
                    DistanceSquared(spot.X, spot.Y, x, y) <=
                    (long)AutonomousRouteThreatPolicy.RoadDeathRadius * AutonomousRouteThreatPolicy.RoadDeathRadius)
                    return true;
            return false;
        }
        public const long RouteThreatHoldLimitMilliseconds = 75_000;

        /// <summary>
        /// A bot that gave up its camp for a monster on the road walks back out of that monster's reach before it plans
        /// again (2026-11-06: AbandonCamp stopped it where it stood, already at the edge of the aggro range, for the 30 s
        /// replan wait; "Retarget Murgar" at 22:39:15, dead at 22:39:26).
        /// </summary>
        private static void StepBackFrom(GameBot bot, IPathfindingMgr nav, Zone zone, GameNPC threat, int keepAway)
        {
            float dx = bot.X - threat.X, dy = bot.Y - threat.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance < 1f || distance >= keepAway)
                return;
            float reach = keepAway + 300;
            Vector3 wanted = new(threat.X + dx / distance * reach, threat.Y + dy / distance * reach, bot.Z);
            if (AutonomousNavigationSurface.TryFloor(nav, zone, wanted, out Vector3 floor))
                bot.PathTo(floor, bot.MaxSpeed);
        }

        /// <summary>
        /// Open-world route threat awareness (see AutonomousRouteThreatPolicy). Called right before
        /// a travelling bot issues its walk order. True consumes this travel turn (a pull started,
        /// the bot is holding, or the camp was given up); false lets the walk continue, with a
        /// detour waypoint set when the bot is going around a monster.
        /// </summary>
        private bool GuardOpenWorldTravel(GameBot bot, Vector3 destination)
        {
            if (!AutonomousRouteThreatPolicy.Enabled || bot.Brain is not BotBrain brain ||
                bot.CurrentZone == null || bot.CurrentZone.IsDungeon || bot.CurrentRegion?.IsDungeon == true ||
                bot.IsOnStableMasterRoute || bot.IsPlayerLedGroup || brain.HasAggro || bot.InCombat || bot.IsAttacking ||
                AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) ||
                AutonomousRealmRaid.GetView(bot.Group) != null)
                return false;
            bool grouped = _groupDirective?.IsDynamic == true;
            if (grouped && _groupDirective.Leader != bot)
                return false;
            long now = GameLoop.GameLoopTime;
            if (now < _nextRouteThreatScanTick)
                return false;
            // Keep looking while walking a detour: the way to the sidestep is scanned too, and a
            // new monster there is fought or makes the bot give up (no detour inside a detour).
            bool detouring = _routeRecoveryWaypoint.HasValue;
            Vector3 scanTarget = _routeRecoveryWaypoint ?? destination;
            _nextRouteThreatScanTick = now + AutonomousRouteThreatPolicy.ScanMilliseconds + bot.ObjectID % 500;

            IPathfindingMgr nav = PathfindingProvider.Instance;
            Zone zone = bot.CurrentZone;
            if (!nav.IsAvailable || !nav.HasNavmesh(zone))
                return false;

            Span<Vector3> route = stackalloc Vector3[257];
            route[0] = new(bot.X, bot.Y, bot.Z);
            int count = bot.movementComponent.CopyUpcomingPath(scanTarget, route[1..]) + 1;
            if (count < 2)
            {
                // Before the first path exists: the straight leg toward the target.
                route[1] = scanTarget;
                count = 2;
            }
            ReadOnlySpan<Vector3> corridor = route[..count];

            // owner 2026-11-10 (P2): a solo bot whose real path passes a live death hotspot gives this camp up and plans
            // another (bounded by the replan budget; logged apart from route-threat give-ups)
            if (!grouped && bot.Group == null && _camp != null &&
                HotspotOnPath(bot, corridor, now, out int hotspotLeg) is { } pathHotspot)
            {
                Log.Info($"AUTONOMOUS_HOTSPOT_ROUTE_REPLAN bot={bot.Name} id={bot.DatabaseID} level={bot.Level} " +
                         $"hotspot={pathHotspot.Region}:{pathHotspot.X},{pathHotspot.Y} killerLevel={pathHotspot.KillerLevel} " +
                         $"camp={_camp.Id} leg={hotspotLeg} corners={count} zone=\"{zone.Description}\"");
                RememberNoGo(pathHotspot.Region, pathHotspot.X, pathHotspot.Y);
                if (_camp.RegionId > 0)
                    RememberNoGo(_camp.RegionId, _camp.X, _camp.Y);
                AbandonCamp(bot, $"Route passes a death hotspot at {pathHotspot.X},{pathHotspot.Y}");
                return true;
            }

            eCharacterClass characterClass = (eCharacterClass)(bot.CharacterClass?.ID ?? 0);
            bool ranged = BotSpellPower.IsOffensiveCaster(bot) ||
                          characterClass is eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger;
            int margin = ranged ? AutonomousRouteThreatPolicy.RangedMargin : AutonomousRouteThreatPolicy.MeleeMargin;

            // owner 2026-11-10: the aggro-level filter is for solo bots only; group leaders see the same threats as before
            bool soloBot = !grouped && bot.Group == null;
            GameNPC[] threats = bot.GetNPCsInRadius((ushort)AutonomousRouteThreatPolicy.ScanRadius)
                .Where(npc => IsExperienceMonster(npc) && npc.IsAlive && !npc.InCombat &&
                              npc.Brain is StandardMobBrain mob &&
                              AutonomousRouteThreatPolicy.CountsByAggroLevel(soloBot, mob.AggroLevel) &&
                              mob.AggroRange > 0 && mob.CanAggroTarget(bot))
                .OrderBy(npc => bot.GetDistanceTo(npc)).Take(24).ToArray();
            GameNPC blocker = null;
            float first = float.MaxValue;
            foreach (GameNPC npc in threats)
            {
                var mob = (StandardMobBrain)npc.Brain;
                Vector3 position = new(npc.X, npc.Y, npc.Z);
                if (Vector2.Distance(new(position.X, position.Y), new(destination.X, destination.Y)) <=
                    AutonomousRouteThreatPolicy.CampSkipRadius)
                    continue;
                // The monster this detour is already going around.
                if (detouring && _routeThreatDetours.ContainsKey(npc))
                    continue;
                if (_unreachableRouteThreats.Contains(npc))
                    continue;
                if (AutonomousDungeonPolicy.IntersectsCorridor(corridor, position, mob.AggroRange + margin,
                        AutonomousRouteThreatPolicy.LookAhead, out float along) && along < first)
                {
                    blocker = npc;
                    first = along;
                }
            }
            if (blocker == null)
            {
                PruneRouteThreatDetours();
                return false;
            }

            var blockerBrain = (StandardMobBrain)blocker.Brain;
            Vector3 blockerPosition = new(blocker.X, blocker.Y, blocker.Z);
            // pack: the wide count (aggro circles touch), as before; linked: those whose own aggro covers a fight at the
            // blocker (owner 2026-11-10, gamebots never bring friends). linked <= pack.
            int pack = 1, linked = 1;
            foreach (GameNPC other in threats)
            {
                if (other == blocker || other.Brain is not StandardMobBrain otherBrain)
                    continue;
                float otherDistance = other.GetDistanceTo(blocker);
                float dz = other.Z - blocker.Z;
                if (!AutonomousRouteThreatPolicy.InWidePack(otherDistance, dz, blockerBrain.AggroRange, otherBrain.AggroRange))
                    continue;
                pack++;
                if (AutonomousRouteThreatPolicy.JoinsFight(otherDistance, dz, otherBrain.AggroRange, margin))
                    linked++;
            }
            ConColor con = ConLevels.GetConColor(bot.GetConLevel(blocker));
            bool bossLike = blocker is IGameEpicNpc ||
                            AutonomousPveTargetPolicy.IsUnreachableFlyer(blocker.Flags, blocker.Z, bot.Z) && !ranged;

            Vector3? detour = detouring ? null : FindRouteThreatDetour(nav, zone, corridor, blocker,
                blockerBrain.AggroRange, margin, destination, threats);
            int detours = _routeThreatDetours.GetValueOrDefault(blocker);
            bool mayRetarget = _camp != null &&
                               now - _lastRouteThreatRetargetTick >= AutonomousRouteThreatPolicy.RetargetCooldownMilliseconds;
            // owner 2026-11-08: a solo bot goes around a pack, or a monster where it just died on the road, when it can.
            // Owner 2026-11-10: Decide gets the linked count for a solo GREEN monster; the detour rule keeps the wide pack.
            RouteThreatAction action = AutonomousRouteThreatPolicy.DecideRoute(con, grouped, pack, linked, bossLike,
                detour.HasValue, detours, mayRetarget, lowLevelSolo: !grouped && bot.Level < 5,
                nearOwnRecentRoadDeath: !grouped && NearOwnRecentRoadDeath(blocker.CurrentRegionID, blocker.X, blocker.Y, now),
                out int decisionPack);
            // owner 2026-11-10: a solo bot goes around a stealthed or ranged monster (pixie scouts) when it can; with no way
            // around the Pull stands (design P6), so this never makes a new give-up
            bool sneaky = (blocker.Flags & GameNPC.eFlags.STEALTH) != 0 ||
                          blocker.Inventory?.GetItem(eInventorySlot.DistanceWeapon) != null;
            RouteThreatAction beforeSneaky = action;
            action = AutonomousRouteThreatPolicy.AvoidSneaky(action, !soloBot, sneaky, detour.HasValue, detours);
            if (action != beforeSneaky)
                Log.Info($"AUTONOMOUS_ROUTE_THREAT_SNEAKY bot={bot.Name} id={bot.DatabaseID} level={bot.Level} " +
                         $"target=\"{blocker.Name}\" con={con} stealth={(blocker.Flags & GameNPC.eFlags.STEALTH) != 0} " +
                         $"was={beforeSneaky} now={action} at={blocker.X},{blocker.Y},{blocker.Z}");
            if (action == RouteThreatAction.Ignore)
                return false;

            Log.Info($"AUTONOMOUS_ROUTE_THREAT bot={bot.Name} id={bot.DatabaseID} class=\"{bot.ClassName}\" level={bot.Level} " +
                     $"group={(grouped ? _groupDirective.GroupId : "solo")} action={action} target=\"{blocker.Name}\" " +
                     $"targetLevel={blocker.Level} con={con} pack={pack} aggro={blockerBrain.AggroRange} along={first:0} " +
                     $"detours={detours} zone=\"{zone.Description}\" at={blocker.X},{blocker.Y},{blocker.Z} " +
                     $"linked={linked} decisionPack={decisionPack}");

            switch (action)
            {
                case RouteThreatAction.Detour:
                    _routeThreatDetours[blocker] = detours + 1;
                    _routeRecoveryWaypoint = detour.Value;
                    _routeRecoveryArrivalRadius = 60;
                    _nextMoveOrderTick = 0;
                    bot.ForcePathReplot();
                    SetStatus(bot, $"Going around {blocker.Name}", GoalText(),
                        $"Taking a way around the {(pack > 1 ? $"pack of {pack}" : "monster")} on the route instead of walking into its aggro",
                        blocker.Name, _camp?.ZoneName ?? string.Empty);
                    return false;

                case RouteThreatAction.Retarget:
                    _lastRouteThreatRetargetTick = now;
                    RememberNoGo(blocker.CurrentRegionID, blocker.X, blocker.Y);   // the next camp doesn't lead back past it
                    if (_camp != null && _camp.RegionId > 0)
                        RememberNoGo(_camp.RegionId, _camp.X, _camp.Y);   // nor this camp, behind it
                    // owner 2026-11-10: solo give-ups are shared; 3 bots at one spot make it a block for 30 minutes
                    if (!grouped && bot.Group == null && AutonomousRouteBlockMemory.Enabled &&
                        AutonomousRouteBlockMemory.RecordRetarget(blocker.CurrentRegionID, blocker.X, blocker.Y, bot.DatabaseID,
                            bot.Level, now, out bool newBlock, out int blockBots) is { } block && newBlock)
                        Log.Info($"AUTONOMOUS_ROUTE_BLOCK region={block.Region} at={block.X},{block.Y} maxBotLevel={block.MaxBotLevel} " +
                                 $"bots={blockBots} target=\"{blocker.Name}\" by={bot.Name} zone=\"{zone.Description}\"");
                    AbandonCamp(bot, $"Route blocked by {blocker.Name} (level {blocker.Level}, {pack} together) with no way around");
                    StepBackFrom(bot, nav, zone, blocker, blockerBrain.AggroRange + margin);
                    return true;

                default: // Pull
                    if (!AutonomousNavigationSurface.TryFloor(nav, zone, blockerPosition, out Vector3 blockerFloor) ||
                        !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, new(bot.X, bot.Y, bot.Z), blockerFloor))
                    {
                        _unreachableRouteThreats.Add(blocker);
                        Log.Info($"AUTONOMOUS_ROUTE_THREAT_UNREACHABLE bot={bot.Name} id={bot.DatabaseID} target=\"{blocker.Name}\" " +
                                 $"at={blocker.X},{blocker.Y},{blocker.Z} zone=\"{zone.Description}\"");
                        return false;
                    }
                    _routeThreatPullRepeats = blocker == _lastRouteThreatPull && now - _lastRouteThreatPullTick < 45_000
                        ? _routeThreatPullRepeats + 1 : 0;
                    _lastRouteThreatPull = blocker;
                    _lastRouteThreatPullTick = now;
                    if (_routeThreatPullRepeats >= RouteThreatPullAttempts)
                    {
                        _unreachableRouteThreats.Add(blocker);
                        _routeThreatPullRepeats = 0;
                        Log.Info($"AUTONOMOUS_ROUTE_THREAT_PULL_FAILED bot={bot.Name} id={bot.DatabaseID} target=\"{blocker.Name}\" " +
                                 $"attempts={RouteThreatPullAttempts} at={blocker.X},{blocker.Y},{blocker.Z} zone=\"{zone.Description}\"");
                        return false;
                    }
                    GameBot puller = grouped ? AutonomousBotGroupCoordinator.PartyPuller(bot) ?? bot : bot;
                    if (grouped && !AutonomousBotGroupCoordinator.CanInitiateNewPull(puller))
                    {
                        if (_routeThreatHold != blocker)
                        {
                            _routeThreatHold = blocker;
                            _routeThreatHoldSince = now;
                        }
                        if (now - _routeThreatHoldSince < RouteThreatHoldLimitMilliseconds)
                        {
                            bot.StopMovingOnPath();
                            bot.StopMoving();
                            SetStatus(bot, "Holding before a route threat", GoalText(),
                                $"Waiting for the whole party before clearing {blocker.Name} from the route",
                                blocker.Name, _camp?.ZoneName ?? string.Empty);
                            return true;
                        }
                        // The party could not get ready to pull: go around if there is a way, else walk on and fight it
                        // if it comes (the party defends itself as on any road).
                        _routeThreatHold = null;
                        Log.Info($"AUTONOMOUS_ROUTE_THREAT_HOLD_EXPIRED bot={bot.Name} id={bot.DatabaseID} group={_groupDirective.GroupId} " +
                                 $"target=\"{blocker.Name}\" puller={puller.Name} seconds={RouteThreatHoldLimitMilliseconds / 1000} " +
                                 $"then={(detour.HasValue ? "detour" : "pass")} at={blocker.X},{blocker.Y},{blocker.Z} zone=\"{zone.Description}\"");
                        if (detour.HasValue)
                        {
                            _routeThreatDetours[blocker] = detours + 1;
                            _routeRecoveryWaypoint = detour.Value;
                            _routeRecoveryArrivalRadius = 60;
                            _nextMoveOrderTick = 0;
                            bot.ForcePathReplot();
                            return false;
                        }
                        _unreachableRouteThreats.Add(blocker);
                        return false;
                    }
                    _routeThreatHold = null;
                    _lastEngagedCon = con;
                    _routeInterruptedByCombat = true;
                    AutonomousBotGroupCoordinator.MarkCombatObserved(bot.Group);
                    if (puller != bot && puller.Brain is BotBrain pullerBrain)
                    {
                        // The party's puller opens on it, as it opens camp pulls; the leader holds the party meanwhile.
                        puller.StopMovingOnPath();
                        puller.StopMoving();
                        puller.TargetObject = blocker;
                        pullerBrain.AddToAggroList(blocker, Math.Max(25, blocker.EffectiveLevel * 10));
                        pullerBrain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                        bot.StopMovingOnPath();
                        bot.StopMoving();
                        SetStatus(bot, $"Clearing the route: {blocker.Name}", GoalText(),
                            $"{puller.Name} is pulling a monster ahead on the route", blocker.Name, _camp?.ZoneName ?? string.Empty);
                        return true;
                    }
                    if (bot.Group == null && SoloRangedPull165.TryBegin(bot, blocker))
                    {
                        SetStatus(bot, $"Clearing the route: {blocker.Name}", GoalText(),
                            "Opening from range on a monster ahead and letting it come", blocker.Name, _camp?.ZoneName ?? string.Empty);
                        return true;
                    }
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                    bot.TargetObject = blocker;
                    brain.AddToAggroList(blocker, Math.Max(25, blocker.EffectiveLevel * 10));
                    brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    if (SavageBotCombatPolicy.MustMeleePull(characterClass))
                        BeginSavageMeleePull(bot, blocker);
                    SetStatus(bot, $"Clearing the route: {blocker.Name}", GoalText(),
                        ranged
                            ? "Opening from range on a monster ahead on the route instead of walking into its aggro"
                            : "Taking on a monster ahead on the route before it can jump from behind",
                        blocker.Name, _camp?.ZoneName ?? string.Empty);
                    return true;
            }
        }

        /// <summary>
        /// A reachable sidestep beside the monster that stays out of every nearby monster's aggro
        /// range; the shorter way of the two sides wins.
        /// </summary>
        private static Vector3? FindRouteThreatDetour(IPathfindingMgr nav, Zone zone, ReadOnlySpan<Vector3> corridor,
            GameNPC blocker, int aggroRange, int margin, Vector3 destination, GameNPC[] threats)
        {
            Vector3 start = corridor[0];
            Vector3 position = new(blocker.X, blocker.Y, blocker.Z);
            if (!AutonomousRouteThreatPolicy.NearestOnRoute(corridor, position, AutonomousRouteThreatPolicy.LookAhead,
                    out Vector3 routePoint, out Vector2 direction) ||
                !AutonomousNavigationSurface.TryFloor(nav, zone, start, out Vector3 startFloor))
                return null;
            // Rejoin the route a sidestep's length past the monster (or at the destination if nearer).
            float sidestep = aggroRange + margin + AutonomousRouteThreatPolicy.DetourClearance;
            Vector3 past = new(routePoint.X + direction.X * sidestep * 1.5f, routePoint.Y + direction.Y * sidestep * 1.5f, routePoint.Z);
            Vector3 rejoin = Vector3.Distance(routePoint, destination) < Vector3.Distance(routePoint, past) ? destination : past;
            Vector3? best = null;
            float bestCost = float.MaxValue;
            foreach (int side in new[] { 1, -1 })
            foreach (float alongFraction in AutonomousRouteThreatPolicy.DetourAlongFractions)
            {
                Vector3 candidate = AutonomousRouteThreatPolicy.DetourPoint(position, direction, aggroRange, margin,
                    routePoint.Z, side, alongFraction);
                // Quick filter: both straight legs pass outside every nearby monster's aggro.
                if (threats.Any(npc => npc.Brain is StandardMobBrain mob &&
                        !AutonomousRouteThreatPolicy.LegsClear(start, candidate, rejoin, new(npc.X, npc.Y, npc.Z),
                            mob.AggroRange + margin / 2f)))
                    continue;
                Vector3? floor = nav.GetClosestPoint(zone, candidate, 96, 96, 320, nav.DefaultFilters);
                if (!floor.HasValue || zone != blocker.CurrentRegion?.GetZone((int)floor.Value.X, (int)floor.Value.Y))
                    continue;
                bool inAggro = threats.Any(npc => npc.Brain is StandardMobBrain mob &&
                    Math.Abs(npc.Z - floor.Value.Z) <= 200 &&
                    Vector2.Distance(new(npc.X, npc.Y), new(floor.Value.X, floor.Value.Y)) < mob.AggroRange + margin);
                if (inAggro || !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, startFloor, floor.Value) ||
                    !PathClearOfAggro(nav, zone, startFloor, floor.Value, threats, margin) ||
                    !PathClearOfAggro(nav, zone, floor.Value, rejoin, threats, margin))
                    continue;
                float cost = Vector3.Distance(start, floor.Value) + Vector3.Distance(floor.Value, destination);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = floor.Value;
                }
            }
            return best;
        }

        /// <summary>
        /// The real navmesh path between two points stays outside every monster's aggro range
        /// (half the safety margin may be used).
        /// </summary>
        private static bool PathClearOfAggro(IPathfindingMgr nav, Zone zone, Vector3 from, Vector3 to,
            GameNPC[] threats, int margin)
        {
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[256];
            PathfindingResult result = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, nodes);
            if (result.NodeCount == 0)
                return false;
            int count = Math.Min(result.NodeCount, nodes.Length);
            Span<Vector3> path = stackalloc Vector3[count + 1];
            path[0] = from;
            float length = 0;
            for (int i = 0; i < count; i++)
            {
                path[i + 1] = nodes[i].Position;
                length += Vector3.Distance(path[i], path[i + 1]);
            }
            foreach (GameNPC npc in threats)
            {
                if (npc.Brain is not StandardMobBrain mob)
                    continue;
                if (AutonomousDungeonPolicy.IntersectsCorridor(path, new(npc.X, npc.Y, npc.Z),
                        mob.AggroRange + margin / 2f, length + 1, out _))
                    return false;
            }
            return true;
        }

        private void PruneRouteThreatDetours()
        {
            if (_unreachableRouteThreats.Count > 0)
            {
                _unreachableRouteThreats.RemoveWhere(npc => !npc.IsAlive || npc.ObjectState != GameObject.eObjectState.Active);
                if (_unreachableRouteThreats.Count > 32) _unreachableRouteThreats.Clear();
            }
            if (_routeThreatDetours.Count == 0)
                return;
            foreach (GameNPC npc in _routeThreatDetours.Keys.Where(npc => !npc.IsAlive ||
                         npc.ObjectState != GameObject.eObjectState.Active).ToArray())
                _routeThreatDetours.Remove(npc);
            if (_routeThreatDetours.Count > 32)
                _routeThreatDetours.Clear();
        }
    }
}
