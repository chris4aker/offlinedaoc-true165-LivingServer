using System;
using System.Collections.Generic;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Crowd control on the bot's own target like a veteran player (owner 2026-11-08, BotControlSpells): against a monster
    /// no mez, root or nearsight, a snare only on a runner, a stun only to interrupt a cast or peel the monster off a
    /// non-tank (a long-reuse stun only with three or more adds on the group). Adds are controlled by TryPveAddControl,
    /// TryBardPveAddMez and TryShamanPveAddRoot; player-like enemies keep their PvP control.
    /// </summary>
    public partial class BotBrain
    {
        private bool ControlAllowedOnTarget(Spell spell, GameLiving target)
        {
            BotControlSpells.ControlKind kind = BotControlSpells.Kind(spell);
            if (kind == BotControlSpells.ControlKind.None)
                return true;
            if (target == null || !target.IsAlive)
                return false;
            bool playerLike = BotPvpCrowdControl.PlayerLike(target);
            return BotControlSpells.AllowsOnOwnTarget(kind, playerLike,
                !playerLike && kind == BotControlSpells.ControlKind.Snare && IsRunningMonster(target),
                !playerLike && kind == BotControlSpells.ControlKind.Stun && StunWorthwhileOn(target, spell));
        }

        /// <summary>Moving, low on health and out of melee with whoever it fights (MaintainChaseSprint's runner).</summary>
        private static bool IsRunningMonster(GameLiving target) =>
            BotControlSpells.IsRunner(target.IsMoving, target.HealthPercent, InMeleeWithVictim(target));

        private static bool InMeleeWithVictim(GameLiving target) =>
            target.TargetObject is GameLiving victim && victim.IsAlive &&
            target.IsWithinRadius(victim, Math.Max(target.attackComponent?.AttackRange ?? 0, 150) + 50);

        private bool StunWorthwhileOn(GameLiving target, Spell spell)
        {
            bool casting = target.IsCasting;
            bool peel = !casting && PeelsNonTank(target);
            if (!casting && !peel)
                return false;
            bool longReuse = BotControlSpells.IsLongReuse(spell);
            // three or more adds besides the monster being killed
            bool emergency = longReuse && MonstersOnGroupCached() > EmergencyAddCount;
            return BotControlSpells.StunWorthwhile(casting, peel, longReuse, emergency);
        }

        /// <summary>The monster is on a groupmate who is no tank (or on this bot when a tank is there to take it back).</summary>
        private bool PeelsNonTank(GameLiving target)
        {
            if (target.TargetObject is not GameLiving victim || !victim.IsAlive || victim is not (GamePlayer or GameBot) ||
                BotHealTriage165.IsTank(victim))
                return false;
            if (victim == Body)
                return BotMeleeStylePolicy.HasTankPartner(BotBody);
            return Body.Group?.IsInTheGroup(victim) == true;
        }

        private int _monstersOnGroupCount;
        private long _monstersOnGroupUntil;

        /// <summary>MonstersOnGroup (up to EmergencyAddCount + 1), counted at most once a second per bot.</summary>
        private int MonstersOnGroupCached()
        {
            long now = GameLoop.GameLoopTime;
            if (now >= _monstersOnGroupUntil)
            {
                _monstersOnGroupCount = MonstersOnGroup(EmergencyAddCount + 1);
                _monstersOnGroupUntil = now + 1_000;
            }
            return _monstersOnGroupCount;
        }

        /// <summary>Monsters fighting this bot or its group, counted up to <paramref name="stopAt"/>.</summary>
        private int MonstersOnGroup(int stopAt)
        {
            int count = 0;
            foreach (GameNPC npc in Body.GetNPCsInRadius(1800))
            {
                if (npc?.IsAlive == true && npc is not GameBot && npc.Realm == eRealm.None &&
                    npc.TargetObject is GameLiving victim && (victim == Body || Body.Group?.IsInTheGroup(victim) == true) &&
                    ++count >= stopAt)
                    break;
            }
            return count;
        }

        /// <summary>A groupmate or this bot's pet holds the bot's target in melee.</summary>
        private bool GroupHoldsTarget(GameLiving target)
        {
            if (target?.TargetObject is not GameLiving holder || holder == Body || !holder.IsAlive ||
                !target.IsWithinRadius(holder, 300))
                return false;
            return holder == Body.ControlledBrain?.Body || Body.Group?.IsInTheGroup(holder) == true;
        }

        /// <summary>
        /// owner 2026-11-08: an Eldritch (or anyone) with a held target casts its plain nuke instead of a snare-nuke of
        /// similar power; the snare is wasted on a monster the tank holds.
        /// </summary>
        private void PreferPlainNukeOverSnareNuke(List<Spell> spells, GameLiving target)
        {
            if (spells.Count < 2 || !spells.Exists(spell => spell.SpellType == eSpellType.DamageSpeedDecrease) ||
                BotPvpCrowdControl.PlayerLike(target) || !GroupHoldsTarget(target))
                return;
            Spell plain = null;
            foreach (Spell spell in spells)
                if (spell.SpellType is eSpellType.DirectDamage or eSpellType.Bolt && spell.Radius <= 0 &&
                    (plain == null || spell.Damage > plain.Damage))
                    plain = spell;
            if (plain != null)
                spells.RemoveAll(spell => BotControlSpells.PrefersPlainNuke(spell, plain, true));
        }

        /// <summary>
        /// owner 2026-11-08: a monster nobody touched - nobody really attacking it (its attacker tracker, which holds hits
        /// of the last few seconds; a damage shield on its victim and a mez wearing off do not count), no damage over
        /// time, disease or snare on it, not this bot's own target, not targeted by a groupmate's pet or elemental.
        /// </summary>
        private bool IsUntouchedAdd(GameNPC add, HashSet<GameLiving> petTargets)
        {
            if (add == Body.TargetObject || petTargets != null && petTargets.Contains(add))
                return false;
            var effects = add.effectListComponent;
            bool damagedOrSnared = effects != null &&
                (effects.ContainsEffectForEffectType(eEffect.DamageOverTime) ||
                 effects.ContainsEffectForEffectType(eEffect.Disease) ||
                 effects.ContainsEffectForEffectType(eEffect.MovementSpeedDebuff) && !IsBreakablyControlled(add));
            if (damagedOrSnared)
                return false;
            AttackerTracker tracker = add.attackComponent?.AttackerTracker;
            bool realAttacker = false;
            if (tracker != null && tracker.Count > 0)
            {
                GameLiving victim = add.TargetObject as GameLiving;
                realAttacker = BotControlSpells.HasRealAttacker(tracker.Attackers, victim,
                    victim != null && AttacksTarget(victim, add));
            }
            return BotControlSpells.IsUntouchedAdd(realAttacker, false, false, false);
        }

        /// <summary>
        /// What every pet of the group targets: members' controlled pets, plus pets without an owner link (Theurgist
        /// elementals, bombers) whose brain names a group member as owner. Reads the NPC list the caller already has.
        /// </summary>
        private static HashSet<GameLiving> GroupPetTargets(IEnumerable<GameNPC> npcs, ICollection<GameLiving> members)
        {
            HashSet<GameLiving> targets = null;
            foreach (GameLiving member in members)
                if (member?.ControlledBrain?.Body?.TargetObject is GameLiving petTarget && petTarget.IsAlive)
                    (targets ??= new()).Add(petTarget);
            foreach (GameNPC npc in npcs)
                if (npc?.IsAlive == true && npc.Brain is IControlledBrain pet && npc.TargetObject is GameLiving target &&
                    target.IsAlive && pet.GetLivingOwner() is GameLiving owner && members.Contains(owner))
                    (targets ??= new()).Add(target);
            return targets;
        }
    }
}
