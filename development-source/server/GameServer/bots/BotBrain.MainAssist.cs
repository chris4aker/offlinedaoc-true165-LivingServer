using System.Runtime.CompilerServices;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// True 1.65 Mode: a gamebot party has a main assist, as period groups did. The tank fighting first sets the
    /// party's kill target (its current target); the other members prefer it over their own threat, unless they are
    /// below half health and being hit, then they fight back. Before 2026-11-06 every bot fought its own highest threat,
    /// so damage was spread over several monsters. Player-led companions already assist their player and are unchanged.
    /// Lock-free: one small holder per group, read and written by the members' own thinks. PvE parties only: an RvR
    /// warband keeps its members' own targets.
    /// </summary>
    public partial class BotBrain
    {
        public const int MainAssistLowHealthPercent = 50;
        public const long KillTargetStaleMilliseconds = 10_000;

        private sealed class KillTargetHolder
        {
            public volatile GameLiving Writer;
            public volatile GameLiving Target;
            public long Tick;
        }

        private static readonly ConditionalWeakTable<Group, KillTargetHolder> KillTargets = new();

        private bool UsesGamebotMainAssist =>
            Classic165Mode.IsOn && BotBody is { IsAutonomousWorldBot: true, IsPlayerLedGroup: false, IsTemporaryGroupHelper: false } &&
            Body.Group != null && AutonomousObjectiveAssignments.KindFor(BotBody) == eAutonomousObjectiveKind.GroupPve;

        /// <summary>Pure rule: a member follows the kill target unless it is mesmerized/rooted, or the member is low and being hit.</summary>
        public static bool AssistsKillTarget(bool killTargetValid, bool killTargetControlled, int healthPercent, bool beingHit) =>
            killTargetValid && !killTargetControlled && !(healthPercent < MainAssistLowHealthPercent && beingHit);

        /// <summary>
        /// Pure rule: the stored kill target is replaced only when it is gone, when its own writer offers a new one, when
        /// that writer has left it, or when nobody has confirmed it for a while. A second tank on an add does not take over.
        /// </summary>
        public static bool ReplacesKillTarget(bool storedAlive, bool sameWriter, bool writerStillOnIt, long ageMilliseconds) =>
            !storedAlive || sameWriter || !writerStillOnIt || ageMilliseconds > KillTargetStaleMilliseconds;

        /// <summary>The tank publishes the monster it is fighting as the party's kill target.</summary>
        private void OfferKillTarget(GameLiving target)
        {
            if (!IsTankClass || !UsesGamebotMainAssist || target is not GameNPC { IsAlive: true } ||
                !(Body.IsAttacking || Body.InCombat) || IsBreakablyControlled(target))
                return;
            KillTargetHolder holder = KillTargets.GetValue(Body.Group, _ => new KillTargetHolder());
            long now = GameLoop.GameLoopTime;
            GameLiving stored = holder.Target, writer = holder.Writer;
            if (stored == target && writer == Body)
            {
                holder.Tick = now;
                return;
            }
            if (stored != null && !ReplacesKillTarget(stored.IsAlive && stored.ObjectState == GameObject.eObjectState.Active,
                    writer == Body, writer is { IsAlive: true } && writer.TargetObject == stored, now - holder.Tick))
                return;
            holder.Writer = Body;
            holder.Target = target;
            holder.Tick = now;
        }

        /// <summary>The party's kill target for a non-tank member, or null to keep its own choice.</summary>
        private GameLiving MainAssistTarget(GameLiving directAttacker)
        {
            if (IsTankClass || Body.Group is not { } group || !KillTargets.TryGetValue(group, out KillTargetHolder holder) ||
                !UsesGamebotMainAssist)
                return null;
            GameLiving target = holder.Target;
            bool valid = target != null && GameLoop.GameLoopTime - holder.Tick <= KillTargetStaleMilliseconds &&
                CanDefendAgainst(target) && Body.IsWithinRadius(target, GROUP_DEFENSE_ASSIST_RADIUS);
            if (!AssistsKillTarget(valid, valid && IsBreakablyControlled(target), Body.HealthPercent, directAttacker != null))
                return null;
            if (!AggroList.ContainsKey(target))
                AddToAggroList(target, 1);
            return target;
        }
    }
}
