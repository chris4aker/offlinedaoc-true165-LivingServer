using DOL.GS;
using DOL.GS.Spells;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        /// <summary>
        /// Owner 2026-11-10: an autonomous party (no player) holds out-of-combat buffs, its self shield and pet buffs while
        /// it walks (BotTravelCastHold). Cheap: one pass over at most eight members, only when upkeep would cast.
        /// </summary>
        internal bool HoldsUpkeepForGroupTravel()
        {
            GameBot bot = BotBody;
            Group group = bot?.Group;
            bool autonomousParty = bot is { IsAutonomousWorldBot: true, IsPlayerLedGroup: false } && group != null;
            if (!autonomousParty)
                return false;
            bool fighting = bot.InCombat || bot.IsAttacking || HasAggro;
            if (!fighting)
                foreach (GameLiving member in group.GetMembersInTheGroup())
                    if (member.IsAlive && (member.InCombat || member.IsAttacking)) { fighting = true; break; }
            GameLiving leader = group.LivingLeader ?? bot;
            bool walking = bot.IsMoving || leader is { IsAlive: true, IsMoving: true };
            // Owner 2026-11-10 (review): the settle runs from the leader's real last step, not from when a think saw it
            // walk (thinks come 2.6-5 s apart at 1,500 bots); sitting or resting is a known stop and ends it at once.
            bool restingStop = bot.IsSitting || bot.IsRecoveryResting || leader.IsSitting || leader is GameBot { IsRecoveryResting: true };
            long lastWalk = leader.movementComponent?.LastWalkTick ?? 0;
            return BotTravelCastHold.Holds(true, fighting, walking, restingStop,
                lastWalk <= 0 ? -1 : GameLoop.GameLoopTime - lastWalk);
        }

        /// <summary>
        /// GitHub #28 review (owner 2026-11-10): a heal over time is not put on a target that already has one running,
        /// from anyone and of any strength, or that a groupmate bot is casting one at right now (no stacking, no overwrite).
        /// </summary>
        private bool HotRunningOrComing(GameLiving target, Spell hot)
        {
            if (target == null || hot == null ||
                (BotHotPrecheck.ConflictRule ? HotPrecheckBlocks(target, hot) : LivingHasEffect(target, hot)))
                return true;
            // Owner 2026-11-10 (review): only a heal over time of the same kind counts (BotHealTriage165.SameKindHot);
            // Shaman regenerations share the health regen family with songs and other regen buffs
            eEffect family = EffectHelper.GetEffectFromSpell(hot);
            bool running = false;
            if (family is not (eEffect.Unknown or eEffect.Pet or eEffect.DirectDamage))
                foreach (ECSGameSpellEffect effect in target.effectListComponent.GetSpellEffects(family))
                    if (SameKind(effect?.SpellHandler?.Spell))
                    {
                        running = true;
                        break;
                    }
            bool coming = false;
            // Owner 2026-11-10 (review): the group read once; another thread can clear Body.Group mid-turn
            if (!running && Body.Group is Group group)
                foreach (GameLiving member in group.GetMembersInTheGroup())
                    if (member != Body && member is GameBot mate &&
                        (CastsSameKindAt(mate.castingComponent?.SpellHandler) ||
                         CastsSameKindAt(mate.castingComponent?.QueuedSpellHandler)))
                    {
                        coming = true;
                        break;
                    }
            return !BotHealTriage165.HotWorthCasting(running, coming, target.HealthPercent);

            bool SameKind(Spell other) => BotHealTriage165.SameKindHot(hot, other, IsOwnHot(other));
            bool CastsSameKindAt(ISpellHandler handler) => handler?.Spell != null && handler.Target == target && SameKind(handler.Spell);
        }

        /// <summary>
        /// Owner 2026-11-10 (round-2 review): LivingHasEffect for a heal over time, but a present effect of the family
        /// blocks only when the server would make it conflict (BotHotPrecheck.PresentBlocksHot). Own cast and queued
        /// checks, the 30 s stronger-buff try and the immunity checks stay as they were.
        /// </summary>
        private bool HotPrecheckBlocks(GameLiving target, Spell hot)
        {
            eEffect family = EffectHelper.GetEffectFromSpell(hot);
            if (family is eEffect.DirectDamage or eEffect.Pet or eEffect.Unknown)
                return false;
            ISpellHandler casting = Body.castingComponent.SpellHandler;
            if (casting != null && casting.Spell.ID == hot.ID && casting.Target == target)
                return true;
            ISpellHandler queued = Body.castingComponent.QueuedSpellHandler;
            if (queued != null && queued.Spell.ID == hot.ID && queued.Target == target)
                return true;
            if (EffectListService.GetPulseEffectOnTarget(target, hot) != null)
                return true;
            double ours = hot.Value * System.Math.Max(0.01, Body.Effectiveness);
            bool conflicting = false;
            bool looseBlock = false; // what the old family-wide test would have said
            foreach (ECSGameEffect present in target.effectListComponent.GetEffects(family))
            {
                ECSGameSpellEffect spellPresent = present as ECSGameSpellEffect;
                Spell existing = spellPresent?.SpellHandler?.Spell;
                double theirs = existing == null ? 0 : existing.Value * System.Math.Max(0.01, spellPresent.Effectiveness);
                bool bothConcentration = hot.IsConcentration && spellPresent != null && spellPresent.IsConcentrationEffect();
                if (existing == null || BotHotPrecheck.Conflicts(hot, existing))
                    conflicting = true;
                if (hot.Value > 0 && BotHotPrecheck.PresentBlocksHot(hot, existing, ours, theirs, bothConcentration))
                    return true;
                looseBlock |= existing == null || existing.ID == hot.ID || bothConcentration || theirs >= ours;
            }
            if (conflicting && (hot.Value <= 0 || !TakeStrongerBuffTry(target, hot)))
                return true;
            if (HotImmune(EffectHelper.GetImmunityEffectFromSpell(hot)) || HotImmune(EffectHelper.GetNpcImmunityEffectFromSpell(hot)))
                return true;
            if (looseBlock)
                NoteHotStacksPastNonConflict(target, hot);
            return false;

            bool HotImmune(eEffect immunity) =>
                immunity is not eEffect.Unknown && EffectListService.GetEffectOnTarget(target, immunity) != null;
        }

        /// <summary>Owner 2026-11-10 (round 3): this check runs every think (pre-HoT scans), so the line is kept once per
        /// HoT spell per <see cref="HotStackLogMilliseconds"/> across all bots, not once a minute per bot.</summary>
        public const long HotStackLogMilliseconds = 30 * 60_000;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> HotStackLogNext = new();

        public static bool TakeHotStackLogSlot(int spellId, long now)
        {
            if (HotStackLogNext.TryGetValue(spellId, out long next) && now < next)
                return false;
            HotStackLogNext[spellId] = now + HotStackLogMilliseconds;
            return true;
        }

        /// <summary>A heal over time the old family test would have held back (rare log, see TakeHotStackLogSlot).</summary>
        private void NoteHotStacksPastNonConflict(GameLiving target, Spell hot)
        {
            if (!TakeHotStackLogSlot(hot.ID, GameLoop.GameLoopTime))
                return;
            log.Info($"BOT_HOT_STACKS_NONCONFLICT bot=\"{Body?.Name}\" spell=\"{hot.Name}\" group={hot.EffectGroup} " +
                     $"type={hot.SpellType} target=\"{target.Name}\"");
        }

        /// <summary>One of this bot's own heal-over-time spells (the four HoT slots, GameBot heal sorting).</summary>
        private bool IsOwnHot(Spell spell)
        {
            GameBot bot = BotBody;
            return spell != null && bot != null &&
                (spell.ID == bot.HealOverTime?.ID || spell.ID == bot.HealOverTimeInstant?.ID ||
                 spell.ID == bot.HealOverTimeGroup?.ID || spell.ID == bot.HealOverTimeInstantGroup?.ID);
        }
    }
}
