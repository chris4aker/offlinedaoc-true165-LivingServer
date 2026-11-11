using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DOL.GS;

/// <summary>
/// Party puller camp turn (owner 2026-11-10): the pull gate can name what holds it, a too-far target can move the
/// party's camp point (bounded), and a party that stays stalled ends cleanly before the 15-minute watchdog teleport.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    /// <summary>The formation radius the pull gate uses (CohesionRadius).</summary>
    public const int PartyCohesionRadius = CohesionRadius;

    private sealed class PartyPullState
    {
        public long StallSince;
        public string StallCampId = string.Empty;
        public bool StallLogged;
        public long LastObservedTick;
        public long XpSampleTick;
        public long Xp;
        public string AdvanceCampId = string.Empty;
        public readonly List<long> AdvanceTicks = new();
    }

    private static readonly ConditionalWeakTable<Session, PartyPullState> PartyPullStates = new();

    /// <summary>
    /// CanInitiateNewPull with the first failing gate named, same gates in the same order (owner 2026-11-10).
    /// The bool overload wraps this one, so the logged gate can never drift from the real one.
    /// </summary>
    public static bool CanInitiateNewPull(GameBot bot, bool corridorBlocker, out string gate)
    {
        gate = "open";
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup || bot.Group == null)
            return true;
        lock (Sync)
        {
            Group group = bot.Group;
            if (group == null || !Sessions.TryGetValue(group, out Session session))
            {
                gate = "no-session";
                return false;
            }
            // Waiting expedition parties may defend themselves, but cannot
            // initiate a fresh pull ahead of the staging/landing order.
            if (AutonomousRealmRaid.GetTravelView(bot) is { } expedition)
            {
                if (expedition.Hold && !(corridorBlocker && expedition.Crossing))
                {
                    gate = "expedition-hold";
                    return false;
                }
                // Only locally present living members take part in puller
                // selection. A casualty or reinforcement across the world is
                // not a veto on every surviving party's combat.
                var expeditionPuller = ChoosePuller(session, BotMembers(group));
                bool expeditionReady = bot == expeditionPuller && bot.HealthPercent >= 70;
                gate = expeditionReady ? "open" : "expedition-puller";
                return expeditionReady;
            }
            GameBot[] members = BotMembers(group);
            GameBot leader = ChooseLeader(session, members);
            GameBot puller = ChoosePuller(session, members);
            if (leader == null)
            {
                gate = "no-leader";
                return false;
            }
            bool combatActor = AutonomousRvrStaging.UsesIndependentCombatActors(session.ObjectiveKind) || bot == puller;
            if (IsAssemblyPhase(session.Phase)) { gate = "assembly"; return false; }
            if (session.Recovery.IsRegrouping) { gate = "regrouping"; return false; }
            if (!combatActor) { gate = "not-puller"; return false; }
            if (session.Recovery.HasCasualty(RecoveryMembers(session, members, false))) { gate = "casualty"; return false; }
            if (session.RecoveringBetweenPulls) { gate = "recovering-between-pulls"; return false; }
            if (!MembersReadyForPull(members))
            {
                gate = members.Length < 2 ? "size" : members.Select(member => AutonomousPartyPullCamp.ResourceGate(member.Name,
                        member.HealthPercent, member.ManaPercent, member.EndurancePercent, member.MaxMana > 0, Classic165Mode.IsOn))
                    .FirstOrDefault(text => text != null) ?? "resources";
                return false;
            }
            if (session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve && !HasActivePveComposition(session, members))
            {
                gate = "composition";
                return false;
            }
            if (members.Length < 2) { gate = "size"; return false; }
            int radius = PullCohesionRadius(corridorBlocker, bot.CurrentZone?.IsDungeon == true);
            foreach (GameBot member in members)
            {
                bool sameRegion = member.CurrentRegionID == leader.CurrentRegionID;
                string placement = AutonomousPartyPullCamp.PlacementGate(member.Name, member.IsOnStableMasterRoute,
                    sameRegion, sameRegion ? member.GetDistanceTo(leader) : int.MaxValue, radius);
                if (placement != null)
                {
                    gate = placement;
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Whether this party may still move its camp point toward a too-far target (2 per camp per 10 minutes).</summary>
    public static bool PartyCampAdvanceAllowed(GameBot puller, string campId)
    {
        if (puller?.Group == null) return false;
        lock (Sync)
        {
            if (!TryGetSession(puller.Group, out Session session) || session.Camp?.Id != campId || session.Camp.IsDungeon ||
                session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve)
                return false;
            PartyPullState state = PartyPullStates.GetOrCreateValue(session);
            if (!string.Equals(state.AdvanceCampId, campId, StringComparison.Ordinal)) return true;
            return AutonomousPartyPullCamp.CanAdvanceCamp(state.AdvanceTicks, GameLoop.GameLoopTime);
        }
    }

    /// <summary>
    /// Moves the shared camp point onto the floor at a too-far target (owner 2026-11-10). Like TryRepositionEmptyCamp:
    /// same camp identity and task clock; the leader walks the formation in with the ordinary camp travel and stops
    /// about 1,250 short, which puts the puller inside its pull range. Puller only, outdoors only, bounded.
    /// </summary>
    public static bool TryAdvanceCampTowardTarget(GameBot puller, string campId, Vector3 point, string targetName,
        long tooFarMilliseconds, out int advancesThisCamp)
    {
        advancesThisCamp = 0;
        if (puller?.Group == null)
            return false;
        lock (Sync)
        {
            if (!TryGetSession(puller.Group, out Session session) || session.Camp?.Id != campId || session.Camp.IsDungeon ||
                session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve || session.Recovery.IsRegrouping)
                return false;
            GameBot[] members = BotMembers(session.Group);
            if (ChoosePuller(session, members) != puller || !HasActivePveComposition(session, members))
                return false;
            PartyPullState state = PartyPullStates.GetOrCreateValue(session);
            long now = GameLoop.GameLoopTime;
            if (!string.Equals(state.AdvanceCampId, campId, StringComparison.Ordinal))
            {
                state.AdvanceCampId = campId;
                state.AdvanceTicks.Clear();
            }
            if (!AutonomousPartyPullCamp.CanAdvanceCamp(state.AdvanceTicks, now))
                return false;
            state.AdvanceTicks.Add(now);
            advancesThisCamp = state.AdvanceTicks.Count;

            SharedCamp previous = session.Camp;
            session.Camp = previous with
            {
                X = (int)Math.Round(point.X),
                Y = (int)Math.Round(point.Y),
                Z = (int)Math.Round(point.Z)
            };
            session.Phase = "Traveling";
            Log.Info("AUTONOMOUS_GROUP_CAMP_ADVANCED " + JsonSerializer.Serialize(new
            {
                group = session.Id,
                camp = previous.Id,
                target = previous.MonsterName,
                pullTarget = targetName,
                zone = previous.ZoneName,
                puller = puller.Name,
                tooFarSeconds = Math.Max(0, tooFarMilliseconds / 1000),
                advancesThisCamp,
                from = new { previous.X, previous.Y, previous.Z },
                to = new { session.Camp.X, session.Camp.Y, session.Camp.Z }
            }));
            WriteSessionMetadata(session, members);
            return true;
        }
    }

    /// <summary>
    /// Pull-stall monitor, run inside UpdateSession under the lock (owner 2026-11-10). O(members) every 5 s at most;
    /// no database or world scans. Returns false when it ended the party.
    /// </summary>
    private static bool ObservePartyPullStall(Session session, GameBot[] members, bool groupCombatActive)
    {
        if (!AutonomousPartyPullCamp.StallLog && !AutonomousPartyPullCamp.StallEnd ||
            session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve || session.Ending || members.Length == 0)
            return true;
        // Realm expeditions hold on their own staging orders; the watchdog path skips them too.
        if (AutonomousRealmRaid.GetView(session.Group) != null)
        {
            if (PartyPullStates.TryGetValue(session, out PartyPullState held)) held.StallSince = 0;
            return true;
        }
        PartyPullState state = PartyPullStates.GetOrCreateValue(session);
        long now = GameLoop.GameLoopTime;
        long gap = state.LastObservedTick == 0 ? 0 : now - state.LastObservedTick;
        state.LastObservedTick = now;
        bool progress = groupCombatActive;
        if (now >= state.XpSampleTick)
        {
            // live experience, as the stuck watchdog reads it (the record only refreshes on save; owner 2026-11-10)
            long xp = members.Sum(member => member?.Experience ?? 0L);
            if (state.XpSampleTick != 0 && xp != state.Xp) progress = true;
            state.Xp = xp;
            state.XpSampleTick = now + 5_000;
        }
        string campId = session.Camp?.Id ?? string.Empty;
        bool sameCamp = campId.Length > 0 && string.Equals(campId, state.StallCampId, StringComparison.Ordinal);
        long since = AutonomousPartyPullCamp.NextStallClock(state.StallSince, campId.Length == 0 ? string.Empty : session.Phase,
            sameCamp, progress, gap, now);
        if (since != state.StallSince)
        {
            if (since == 0 || since == now) state.StallLogged = false;
            state.StallSince = since;
            state.StallCampId = campId;
        }
        if (since == 0) return true;
        PullStallAction action = AutonomousPartyPullCamp.PullStallStep(now - since);
        if (action == PullStallAction.None) return true;
        if (action == PullStallAction.Log && (state.StallLogged || !AutonomousPartyPullCamp.StallLog)) return true;
        if (action == PullStallAction.End && !AutonomousPartyPullCamp.StallEnd)
        {
            if (state.StallLogged || !AutonomousPartyPullCamp.StallLog) return true;
            action = PullStallAction.Log;
        }

        GameBot puller = ChoosePuller(session, members);
        string gate = puller == null ? "no-puller" : CanInitiateNewPull(puller, false, out string named) ? "open" : named;
        string pullerActivity = puller?.PersistentRecord?.Activity ?? string.Empty;
        if (gate == "open") gate = $"open:{pullerActivity}";
        int minutes = (int)((now - since) / 60_000);
        string payload = JsonSerializer.Serialize(new
        {
            group = session.Id,
            phase = session.Phase,
            camp = campId,
            target = session.Camp?.MonsterName ?? string.Empty,
            zone = session.Camp?.ZoneName ?? string.Empty,
            puller = puller?.Name ?? string.Empty,
            pullerActivity,
            gate,
            minutes,
            ending = action == PullStallAction.End
        });
        if (action == PullStallAction.Log)
        {
            state.StallLogged = true;
            Log.Info("AUTONOMOUS_GROUP_PULL_STALL " + payload);
            return true;
        }
        Log.Warn("AUTONOMOUS_GROUP_PULL_STALL_END " + payload);
        FinishGroupTask(session, $"Pull stalled {minutes} minutes at {campId}: {gate}");
        return false;
    }
}
