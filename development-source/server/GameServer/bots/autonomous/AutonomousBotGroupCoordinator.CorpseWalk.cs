using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS;

public static partial class AutonomousBotGroupCoordinator
{
    // Item 17 (2026-11-11 overnight run): resurrection timeouts rose from 4 to 33 an hour. 176 of 235 had a living
    // resurrector of the party in the region but out of spell range (median 2,809 units), and 153 of those happened while
    // the party was regrouping: BotBrain walks a healer to a distant corpse, but never during a regroup (so it doesn't chase
    // a corpse back into a failed pull), and the regrouping party stood at the rendezvous or the bindstone while the corpse
    // timed out after 60 s. Now, out of combat, one resurrector of the party is granted a walk to the nearest corpse within
    // sight range - regrouping or not - unless the corpse lies by monsters that outclass the walker (an aggressive or fighting
    // orange-or-worse monster near it, a live death hotspot, or a party danger spot), and every corpse of the party keeps the longer
    // 120 s wait while that walk (or the rest for power at the corpse) is under way. When the walker dies, stalls or the
    // way turns unsafe the grant lapses within seconds and the dead release as before.
    public const int CorpseWalkMaxDistance = WorldMgr.VISIBILITY_DISTANCE;
    public const long CorpseWalkMaxMilliseconds = 100_000;
    public const long CorpseWalkClaimStaleMilliseconds = 5_000;
    public const long CorpseWalkSafetyRecheckMilliseconds = 3_000;
    public const long CorpseWalkRefusalMilliseconds = 30_000;
    public const ushort CorpseThreatRadius = 1_500;

    private sealed class CorpseWalkClaim
    {
        public GameBot Walker;
        public GameLiving Corpse;
        public long Started;
        public long LastSeen;
        public long NextSafetyCheck;
    }

    private sealed class CorpseWalkState
    {
        public CorpseWalkClaim Claim;
        // corpse key -> tick until which no walk to it is granted (unsafe, or a walk to it already ran out)
        public readonly Dictionary<long, long> Refused = new();
        public long NextChoiceTick;
    }
    private static readonly ConditionalWeakTable<Session, CorpseWalkState> CorpseWalks = new();

    /// <summary>Pure rule: a grant is current while its walker is alive in the party, was seen recently and has time left.</summary>
    public static bool IsCorpseWalkCurrent(bool walkerAlive, bool walkerInParty, bool corpseDead, long started, long lastSeen, long now) =>
        walkerAlive && walkerInParty && corpseDead && started > 0 && now >= started &&
        now - lastSeen < CorpseWalkClaimStaleMilliseconds && now - started < CorpseWalkMaxMilliseconds;

    /// <summary>Pure rule: a monster near a corpse makes the walk unsafe when it cons orange or worse to the walker and is
    /// aggressive or already fighting (the walker comes alone; the party is out of combat).</summary>
    public static bool IsCorpseThreat(ConColor conToWalker, bool aggressive, bool fighting) =>
        conToWalker >= ConColor.ORANGE && (aggressive || fighting);

    /// <summary>
    /// BotBrain.TryResurrectGroupMember asks this for each distant dead member: may <paramref name="walker"/> walk to
    /// <paramref name="corpse"/> to raise it? Outside a bot PvE party the old rule stands (walk unless regrouping).
    /// </summary>
    public static bool MayWalkToCorpse(GameBot walker, GameLiving corpse, bool regrouping)
    {
        Group group = walker?.Group;
        if (group == null || corpse == null || !walker.IsAutonomousWorldBot || walker.IsTemporaryGroupHelper || walker.IsPlayerLedGroup ||
            AutonomousRealmRaid.GetView(group) != null)
            return !regrouping;
        long now = GameLoop.GameLoopTime;
        GameLiving[] candidates;
        CorpseWalkState state;
        lock (Sync)
        {
            if (!TryGetSession(group, out Session session) || session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve)
                return !regrouping;
            state = CorpseWalks.GetOrCreateValue(session);
            CorpseWalkClaim claim = state.Claim;
            if (claim != null && !ClaimCurrent(claim, group, now))
            {
                if (now - claim.Started >= CorpseWalkMaxMilliseconds && claim.Corpse is GameBot timedOut)
                    state.Refused[MemberKey(timedOut)] = now + CorpseWalkRefusalMilliseconds;
                state.Claim = claim = null;
            }
            if (claim != null)
            {
                if (claim.Walker != walker)
                    return false;
                if (claim.Corpse != corpse)
                    return false;
                if (now < claim.NextSafetyCheck)
                {
                    claim.LastSeen = now;
                    return true;
                }
            }
            else if (now < state.NextChoiceTick)
                return false;
            candidates = claim != null ? [claim.Corpse] : BotMembers(group)
                .Where(member => member != walker && !member.IsAlive && member.ObjectState == GameObject.eObjectState.Active &&
                                 member.CurrentRegionID == walker.CurrentRegionID &&
                                 walker.IsWithinRadius(member, CorpseWalkMaxDistance) &&
                                 !(state.Refused.TryGetValue(MemberKey(member), out long until) && until > now))
                .OrderBy(walker.GetDistanceTo)
                .Take(2)
                .Cast<GameLiving>()
                .ToArray();
            if (candidates.Length == 0)
            {
                state.NextChoiceTick = now + 2_000;
                return false;
            }
        }

        // The world scan runs outside the party lock, at most for two corpses every few seconds per party.
        GameLiving chosen = null;
        foreach (GameLiving candidate in candidates)
        {
            if (CorpseSafeToApproach(walker, candidate, now, out string reason))
            {
                chosen = candidate;
                break;
            }
            lock (Sync)
            {
                state.Refused[candidate is GameBot refusedBot ? MemberKey(refusedBot) : candidate.ObjectID] = now + CorpseWalkRefusalMilliseconds;
                bool wasWalking = state.Claim?.Walker == walker && state.Claim.Corpse == candidate;
                if (wasWalking)
                    state.Claim = null;
                Log.Info($"AUTONOMOUS_CORPSE_WALK_REFUSED bot=\"{walker.Name}\" level={walker.Level} corpse=\"{candidate.Name}\" " +
                         $"distance={walker.GetDistanceTo(candidate)} walking={wasWalking} reason=\"{reason}\" " +
                         $"at={candidate.CurrentRegionID}:{candidate.X},{candidate.Y}");
            }
        }
        lock (Sync)
        {
            if (chosen == null)
            {
                state.NextChoiceTick = now + 2_000;
                return false;
            }
            CorpseWalkClaim claim = state.Claim;
            if (claim != null && claim.Walker == walker && claim.Corpse == chosen)
            {
                claim.LastSeen = now;
                claim.NextSafetyCheck = now + CorpseWalkSafetyRecheckMilliseconds;
                return chosen == corpse;
            }
            if (claim != null && ClaimCurrent(claim, group, now))
                return false;   // another resurrector took it meanwhile
            state.Claim = new CorpseWalkClaim
            {
                Walker = walker, Corpse = chosen, Started = now, LastSeen = now,
                NextSafetyCheck = now + CorpseWalkSafetyRecheckMilliseconds
            };
            Log.Info($"AUTONOMOUS_CORPSE_WALK_START bot=\"{walker.Name}\" class=\"{walker.ClassName}\" level={walker.Level} " +
                     $"corpse=\"{chosen.Name}\" distance={walker.GetDistanceTo(chosen)} mana={walker.ManaPercent} " +
                     $"regrouping={regrouping} at={chosen.CurrentRegionID}:{chosen.X},{chosen.Y}");
            return chosen == corpse;
        }
    }

    /// <summary>Caller holds Sync. A resurrector of this party is walking to (or resting at) one of its corpses.</summary>
    private static bool CorpseWalkUnderWay(Session session, long now) =>
        CorpseWalks.TryGetValue(session, out CorpseWalkState state) && state.Claim is { } claim &&
        ClaimCurrent(claim, session.Group, now);

    private static bool ClaimCurrent(CorpseWalkClaim claim, Group group, long now) =>
        IsCorpseWalkCurrent(claim.Walker.IsAlive, claim.Walker.Group == group, !claim.Corpse.IsAlive &&
            claim.Corpse.ObjectState == GameObject.eObjectState.Active && claim.Corpse.CurrentRegionID == claim.Walker.CurrentRegionID,
            claim.Started, Math.Max(claim.LastSeen, RestingOrRaisingTick(claim, now)), now);

    // A walker that arrived and sits for power, or is raising, keeps the grant without asking again.
    private static long RestingOrRaisingTick(CorpseWalkClaim claim, long now) =>
        BotGroupSupport.IsRestingForResurrection(claim.Walker, null, now) ||
        claim.Walker.IsCasting && claim.Walker.castingComponent.SpellHandler?.Spell?.SpellType == eSpellType.Resurrect
            ? now : 0;

    /// <summary>Not into the killer: no live death hotspot or party danger spot for the walker's level at the corpse
    /// (or straight on the way), and no aggressive monster that outclasses the walker beside it.</summary>
    private static bool CorpseSafeToApproach(GameBot walker, GameLiving corpse, long now, out string reason)
    {
        if (AutonomousRoadHotspots.Blocks(walker.CurrentRegionID, walker.X, walker.Y, walker.Level,
                corpse.CurrentRegionID, corpse.X, corpse.Y, now))
        {
            reason = "death hotspot at the corpse or on the way";
            return false;
        }
        if (AutonomousGroupDangerRetreat.NearSpot(corpse.CurrentRegionID, corpse.X, corpse.Y, walker.Level, now,
                AutonomousGroupDangerRetreat.ClusterRadius))
        {
            reason = "party danger spot at the corpse";
            return false;
        }
        foreach (GameNPC npc in corpse.GetNPCsInRadius(CorpseThreatRadius))
        {
            if (npc is GameBot || !npc.IsAlive || npc.ObjectState != GameObject.eObjectState.Active ||
                (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) != 0 ||
                !GameServer.ServerRules.IsAllowedToAttack(walker, npc, true))
                continue;
            bool aggressive = npc.Brain is StandardMobBrain mob && mob.AggroLevel > 0 && mob.AggroRange > 0;
            if (IsCorpseThreat(ConLevels.GetConColor(walker.GetConLevel(npc)), aggressive, npc.InCombat))
            {
                reason = $"{npc.Name} (level {npc.Level}) beside the corpse";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }
}
