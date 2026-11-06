using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Mesmerize and stun on adds for every class that has them (owner 2026-10-28, GitHub issue #17: Healers never used
    /// their mesmerize or stun spells, single or area; only Bards controlled adds, through TryBardPveAddMez). An add is a
    /// monster attacking the group that nobody has chosen to kill. An area mez is used only where it cannot reach the
    /// monster being killed (it would put the group's target to sleep) or a monster not in the fight. A stun protects
    /// whoever an add is hitting while they fight something else (a healer or caster). The instant mesmerize and stun with
    /// a ten-minute reuse are kept for three or more adds. Celerity, the Augmentation Healer's 20-second group attack
    /// speed, was never cast: the buff selector bots use has no case for it (it is a fight buff, not upkeep).
    /// True 1.65 (owner 2026-11-06: a Healer never mezzed, whatever its Pacification; Prima calls Pacification great crowd
    /// control): an add is a monster nobody is ATTACKING that is not the group's kill target - a tank only running at it
    /// (FindProtectionTarget targets the add on the healer at once) no longer makes it "taken". The add a mezzer is about
    /// to mez is reserved for a moment (<see cref="BotMezReservations"/>) so tanks and their protection leave it alone,
    /// add control goes before heals unless someone is under 40%, a single add on a healer who cannot finish a cast gets
    /// the instant single-target mez, and an area mez is used whenever the kill target is outside its radius.
    /// </summary>
    public partial class BotBrain
    {
        /// <summary>True 1.65: add control goes before heals unless a groupmate is below this health.</summary>
        public const int AddControlBeforeHealsHealthPercent = 40;
        /// <summary>How long tanks and protection leave an add alone that a mezzer has started to mez (at least).</summary>
        public const long MezReservationMilliseconds = 3_000;

        /// <summary>
        /// Pure rule: a monster attacking the group is somebody's fight, not an add to control. Normal mode: anyone has it
        /// targeted, or it is this bot's target. True 1.65: somebody is attacking it, or it is the group's kill target.
        /// </summary>
        public static bool IsHandledAdd(bool classic165, bool targeted, bool attacked, bool killTarget, bool ownTarget) =>
            classic165 ? attacked || killTarget : targeted || ownTarget;

        /// <summary>
        /// Pure rule: the mesmerize or stun with a ten-minute reuse is kept for three or more adds. True 1.65 also: a
        /// single-target one for a single add when this caster cannot finish a cast (the add is hitting it).
        /// </summary>
        public static bool AllowsLongReuseControl(bool classic165, bool emergency, bool singleTarget, bool castTimeBlocked) =>
            emergency || classic165 && singleTarget && castTimeBlocked;

        /// <summary>Pure rule: True 1.65 add control before heals while every groupmate is at 40% health or more.</summary>
        public static bool AddControlBeforeHeals(bool classic165, int lowestMemberHealthPercent) =>
            classic165 && lowestMemberHealthPercent >= AddControlBeforeHealsHealthPercent;

        /// <summary>Whether a mezzer has just started on this add: tanks, protection and target choice leave it for a moment.</summary>
        internal static bool IsReservedForMez(GameLiving living) => BotMezReservations.IsReserved(living, GameLoop.GameLoopTime);

        /// <summary>The group's kill target: the player's target in a player-led group, else the gamebot party's main assist.</summary>
        private GameLiving GroupKillTarget()
        {
            if (BotBody?.IsPlayerLedGroup == true)
                return AssistedPlayer?.TargetObject as GameLiving;
            return Body.Group is { } group && KillTargets.TryGetValue(group, out KillTargetHolder holder) &&
                GameLoop.GameLoopTime - holder.Tick <= KillTargetStaleMilliseconds ? holder.Target : null;
        }

        /// <summary>A member (or pet) hitting this monster now: swinging within reach of it, or casting a harmful spell at it.</summary>
        private static bool AttacksTarget(GameLiving member, GameLiving target) =>
            member.TargetObject == target &&
            (member.IsAttacking && member.attackComponent != null &&
                member.IsWithinRadius(target, member.attackComponent.AttackRange + 50) ||
             member.IsCasting && member.castingComponent?.SpellHandler is { Spell.IsHarmful: true } handler &&
                handler.Target == target);

        /// <summary>The lowest health of the living groupmates in support reach (the bot's own when solo).</summary>
        private int LowestGroupHealthPercent()
        {
            if (Body.Group == null)
                return Body.HealthPercent;
            int lowest = 100;
            foreach (GameLiving member in Body.Group.GetMembersInTheGroup())
                if (member?.IsAlive == true && member.HealthPercent < lowest && InSupportReach(member))
                    lowest = member.HealthPercent;
            return lowest;
        }

        private const long PveAddControlRetryMilliseconds = 15_000;
        private const long EmergencyRecastMilliseconds = 60_000;
        private const int EmergencyAddCount = 3;
        /// <summary>A healing class keeps this much power for heals, as a real healer does, before it mezzes or hastes.</summary>
        private const int HealerControlPowerPercent = 25;
        private const int HealerCelerityPowerPercent = 30;
        private readonly Dictionary<GameLiving, long> _pveAddControlRetryUntil = new();
        private long _nextPveAddControlLook;
        private long _nextCelerityLook;

        private static bool IsAddControlSpell(Spell spell) =>
            spell?.SpellType is eSpellType.Mesmerize or eSpellType.Mez or eSpellType.Stun;

        private bool TryPveAddControl()
        {
            GameBot bot = BotBody;
            Group group = bot?.Group;
            long now = GameLoop.GameLoopTime;
            // Solo with a pet counts too (class audit 2026-11-05; Prima: a Sorcerer or Minstrel with a charmed pet mezzes
            // the extra monsters, even solo): the pet holds the kill target, the bot keeps the adds asleep.
            GameLiving ownPet = bot?.ControlledBrain?.Body is GameLiving petBody && petBody.IsAlive ? petBody : null;
            if (bot?.CharacterClass == null || bot.CharacterClass.ID == (int)eCharacterClass.Bard ||
                (group == null || group.MemberCount < 2) && ownPet == null || bot.IsIncapacitated || bot.IsCasting && !IsChannellingPetShield ||
                bot.castingComponent?.HasPendingSkillRequests == true || now < _nextPveAddControlLook ||
                BotPartyRoles.IsHealingClass((eCharacterClass)bot.CharacterClass.ID) && bot.ManaPercent < HealerControlPowerPercent)
                return false;
            _nextPveAddControlLook = now + 1_000;

            Spell[] control = bot.Spells?.Where(spell => spell != null && IsAddControlSpell(spell) &&
                    spell.Target == eSpellTarget.ENEMY && spell.Range > 0 && spell.Level <= bot.Level &&
                    bot.GetSkillDisabledDuration(spell) <= 0 && bot.Mana >= bot.PowerCost(spell))
                .ToArray();
            if (control == null || control.Length == 0)
                return false;

            GameLiving[] members = (group?.GetMembersInTheGroup() ?? [bot])
                .Where(member => member?.IsAlive == true && member.CurrentRegion == bot.CurrentRegion)
                .Concat(ownPet != null ? [ownPet] : [])
                .Distinct()
                .ToArray();
            HashSet<GameLiving> selectedTargets = new();
            HashSet<GameLiving> activeTargets = new();
            HashSet<GameLiving> attackedTargets = new();   // True 1.65: what somebody is hitting right now
            foreach (GameLiving member in members)
            {
                if (member.TargetObject is GameLiving target && target.IsAlive)
                {
                    selectedTargets.Add(target);
                    if ((member.IsAttacking || member.InCombat) && target is GameNPC)
                        activeTargets.Add(target);
                    if (AttacksTarget(member, target))
                        attackedTargets.Add(target);
                }
                if (member.ControlledBrain?.Body is GameLiving pet && pet.TargetObject is GameLiving petTarget && petTarget.IsAlive)
                {
                    selectedTargets.Add(petTarget);
                    if (petTarget is GameNPC)
                        activeTargets.Add(petTarget);
                    if (AttacksTarget(pet, petTarget))
                        attackedTargets.Add(petTarget);
                }
            }
            if (activeTargets.Count == 0)
                return false;

            foreach (GameLiving expired in _pveAddControlRetryUntil
                         .Where(pair => now >= pair.Value || pair.Key?.IsAlive != true)
                         .Select(pair => pair.Key).ToArray())
                _pveAddControlRetryUntil.Remove(expired);

            bool classic165 = Classic165Mode.IsOn;
            GameLiving killTarget = classic165 ? GroupKillTarget() : null;
            List<GameNPC> adds = bot.GetNPCsInRadius(1800)
                .Where(npc => npc?.IsAlive == true && npc.Realm == eRealm.None && !BotPvpCrowdControl.PlayerLike(npc) &&
                    !IsUntargetable(npc) && CanDefendAgainst(npc) &&
                    npc.TargetObject is GameLiving victim && members.Contains(victim) &&
                    !IsHandledAdd(classic165, selectedTargets.Contains(npc), attackedTargets.Contains(npc), npc == killTarget,
                        npc == Body.TargetObject) &&
                    !(classic165 && IsReservedForMez(npc)) &&   // another mezzer is on it
                    !npc.IsMezzed && !npc.IsStunned && now >= _pveAddControlRetryUntil.GetValueOrDefault(npc))
                .OrderBy(bot.GetDistanceTo).Take(16).ToList();
            if (adds.Count == 0)
                return false;
            bool emergency = adds.Count >= EmergencyAddCount;

            foreach (GameNPC add in adds)
            {
                bool mezImmune = add.effectListComponent.ContainsEffectForEffectType(eEffect.MezImmunity) ||
                    add.effectListComponent.ContainsEffectForEffectType(eEffect.NPCMezImmunity);
                bool stunImmune = add.effectListComponent.ContainsEffectForEffectType(eEffect.StunImmunity) ||
                    add.effectListComponent.ContainsEffectForEffectType(eEffect.NPCStunImmunity);
                // A stun is protection: only for an add hitting someone who is fighting something else.
                bool protects = add.TargetObject is GameLiving victim && victim.TargetObject != add;

                foreach (Spell spell in control
                             .OrderBy(spell => spell.SpellType == eSpellType.Stun ? 1 : 0)   // a mez lasts longer
                             .ThenBy(spell => spell.Radius > 0 ? 1 : 0)                      // a single target first
                             .ThenByDescending(spell => spell.Level))
                {
                    bool isMez = spell.SpellType != eSpellType.Stun;
                    if (isMez ? mezImmune : (stunImmune || !protects))
                        continue;
                    if (spell.HasRecastDelay && spell.RecastDelay >= EmergencyRecastMilliseconds &&
                        !AllowsLongReuseControl(classic165, emergency, spell.Radius <= 0,
                            bot.IsBeingInterrupted || !CastTimeControlReady(control, isMez)))
                        continue;
                    if (spell.CastTime > 0 && !spell.Uninterruptible && bot.IsBeingInterrupted ||
                        !bot.IsWithinRadius(add, spell.CalculateEffectiveRange(bot)) ||
                        !NeedsOffensiveSpellApplication(add, spell) ||
                        !BotGroupSupport.HasCorpseLineOfSight(bot, add))
                        continue;
                    if (spell.Radius > 0)
                    {
                        // An area mez must not reach a monster the group is killing; neither may wake or pull anything.
                        // True 1.65: the monsters being hit and the kill target (a tank only running at one is no kill).
                        IEnumerable<GameLiving> killed = classic165
                            ? killTarget != null ? attackedTargets.Append(killTarget) : attackedTargets
                            : activeTargets.Concat(selectedTargets);
                        if (isMez && killed.Any(target =>
                                target is GameObject kill && add.IsWithinRadius(kill, spell.Radius + BotAoePolicy.SafetyMargin)))
                            continue;
                        if (EvaluateBlast(add, spell).Vetoed)
                            continue;
                    }

                    if (_pveAddControlRetryUntil.Count >= 32)
                        _pveAddControlRetryUntil.Remove(_pveAddControlRetryUntil.OrderBy(pair => pair.Value).First().Key);
                    _pveAddControlRetryUntil[add] = now + PveAddControlRetryMilliseconds;
                    // True 1.65: tanks and protection leave the add alone while the mez goes off
                    bool reserve = classic165 && isMez;
                    if (reserve)
                        BotMezReservations.Reserve(add, now + Math.Max(MezReservationMilliseconds, spell.CastTime + 1_000));
                    GameObject previous = bot.TargetObject;
                    bot.TargetObject = add;
                    try
                    {
                        if (spell.CastTime > 0)
                        {
                            bot.StopMovingOnPath();
                            bot.StopMoving();
                        }
                        if (!CheckOffensiveSpells(spell))
                        {
                            if (reserve)
                                BotMezReservations.Release(add);
                            continue;
                        }
                        ReportAddControl(spell, add, adds.Count);
                        return true;
                    }
                    finally
                    {
                        bot.TargetObject = previous;
                    }
                }
            }
            return false;
        }

        /// <summary>Whether a castable add-control spell of this kind with a cast time and no long reuse is on hand.</summary>
        private static bool CastTimeControlReady(Spell[] control, bool mez) =>
            control.Any(spell => (spell.SpellType != eSpellType.Stun) == mez && spell.CastTime > 0 && spell.Radius <= 0 &&
                !(spell.HasRecastDelay && spell.RecastDelay >= EmergencyRecastMilliseconds));

        private long _nextAddControlReport;

        private void ReportAddControl(Spell spell, GameNPC add, int adds)
        {
            long now = GameLoop.GameLoopTime;
            if (now < _nextAddControlReport)
                return;
            _nextAddControlReport = now + 30_000;
            log.Info($"BOT_ADD_CONTROL bot=\"{Body.Name}\" class=\"{BotBody.ClassName}\" spell=\"{spell.Name}\" type={spell.SpellType} " +
                     $"radius={spell.Radius} add=\"{add.Name}\" adds={adds} region={Body.CurrentRegionID} companion={BotBody.IsTemporaryGroupHelper}");
        }

        /// <summary>Celerity and its upgrades: a group attack speed buff of 20 seconds with no concentration cost.</summary>
        public static bool IsShortGroupHaste(Spell spell) =>
            spell?.SpellType == eSpellType.CombatSpeedBuff && spell.Target == eSpellTarget.GROUP &&
            !spell.IsConcentration && spell.Duration > 0 && spell.Duration <= 20_000;

        /// <summary>Celerity while a melee groupmate is fighting in melee and has none: below heals, never while being hit.</summary>
        private bool TryGroupCelerity()
        {
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (bot?.Group == null || bot.IsIncapacitated || bot.IsCasting || bot.IsBeingInterrupted ||
                bot.castingComponent?.HasPendingSkillRequests == true || now < _nextCelerityLook ||
                bot.ManaPercent < HealerCelerityPowerPercent)
                return false;
            _nextCelerityLook = now + 1_000;

            Spell celerity = bot.Spells?.Where(spell => IsShortGroupHaste(spell) && spell.Level <= bot.Level &&
                    bot.GetSkillDisabledDuration(spell) <= 0 && bot.Mana >= bot.PowerCost(spell))
                .OrderByDescending(spell => spell.Level).FirstOrDefault();
            if (celerity == null)
                return false;

            int range = Math.Max(1, celerity.CalculateEffectiveRange(bot));
            GameLiving fighter = bot.Group.GetMembersInTheGroup().FirstOrDefault(member =>
                member?.IsAlive == true && member.CurrentRegion == bot.CurrentRegion && bot.IsWithinRadius(member, range) &&
                member.attackComponent?.AttackState == true && member.ActiveWeaponSlot != eActiveWeaponSlot.Distance &&
                member.TargetObject is GameLiving target && target.IsAlive &&
                member.IsWithinRadius(target, member.MeleeAttackRange + 100) && !LivingHasEffect(member, celerity));
            if (fighter == null)
                return false;

            GameObject previous = bot.TargetObject;
            bot.TargetObject = fighter;
            try
            {
                if (celerity.CastTime > 0)
                {
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                }
                return CastCoordinatedBuff(celerity);
            }
            finally
            {
                bot.TargetObject = previous;
            }
        }
    }

    /// <summary>
    /// The add each mezzer has just started to mez (True 1.65), so a tank's protection and every bot's target choice
    /// leave it alone for about three seconds instead of breaking the mez before it lands. Lock-free: one small holder
    /// per monster, written only by mezzers; a global "latest end" lets every other bot skip the lookup when none is live.
    /// </summary>
    public static class BotMezReservations
    {
        private sealed class Holder
        {
            public long Until;
        }

        private static readonly ConditionalWeakTable<object, Holder> Reservations = new();
        private static long _latestUntil;

        public static void Reserve(object add, long until)
        {
            if (add == null)
                return;
            Volatile.Write(ref Reservations.GetValue(add, _ => new Holder()).Until, until);
            long latest;
            while (until > (latest = Volatile.Read(ref _latestUntil)) &&
                   Interlocked.CompareExchange(ref _latestUntil, until, latest) != latest)
            {
            }
        }

        public static void Release(object add)
        {
            if (add != null && Reservations.TryGetValue(add, out Holder holder))
                Volatile.Write(ref holder.Until, 0);
        }

        public static bool IsReserved(object add, long now) =>
            add != null && now < Volatile.Read(ref _latestUntil) &&
            Reservations.TryGetValue(add, out Holder holder) && now < Volatile.Read(ref holder.Until);
    }
}
