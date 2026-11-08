using System.Collections.Generic;
using System.Linq;
using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        private const long ShamanPveRootRetryMilliseconds = 15_000;
        private readonly Dictionary<GameLiving, long> _shamanPveRootRetryUntil = new();
        private long _nextPveAddRootLook;

        /// <summary>
        /// Grouped healers (first the Shaman, now every healing class) root an add
        /// that is running at the party while the party fights a different
        /// monster. Solo healers and the kill target are never rooted in PvE
        /// (damage breaks the root at once). Heals always run first: CheckSpells
        /// calls CheckHeals before this.
        /// owner 2026-11-08: every class with a root (Cabalist, Bonedancer, Druid ...) roots only here, on a loose add
        /// nobody touched; roots never go on the bot's own monster.
        /// </summary>
        private bool TryShamanPveAddRoot()
        {
            GameBot shaman = BotBody;
            Group group = shaman?.Group;
            long now = GameLoop.GameLoopTime;
            if (shaman?.CharacterClass == null ||
                !BardBotCrowdControlPolicy.HasGroupForPveAdd(group?.MemberCount) ||
                shaman.CanCastCrowdControlSpells != true ||
                shaman.IsIncapacitated || shaman.IsCasting ||
                shaman.castingComponent?.HasPendingSkillRequests == true || now < _nextPveAddRootLook)
                return false;
            _nextPveAddRootLook = now + 1_000;

            Spell[] roots = shaman.CrowdControlSpells
                .Where(spell => ShamanBotCombatPolicy.IsRoot(spell) && spell.Target == eSpellTarget.ENEMY &&
                    spell.Radius <= 0 && spell.Level <= shaman.Level)
                .OrderByDescending(spell => spell.Level).ToArray();
            if (roots.Length == 0)
                return false;

            GameLiving[] members = group.GetMembersInTheGroup()
                .Where(member => member?.IsAlive == true && member.CurrentRegion == shaman.CurrentRegion)
                .ToArray();
            if (members.Length < 2)
                return false;

            HashSet<GameLiving> selectedTargets = new();
            HashSet<GameLiving> activeTargets = new();
            foreach (GameLiving member in members)
            {
                if (member.TargetObject is GameLiving target && target.IsAlive)
                {
                    selectedTargets.Add(target);
                    if ((member.IsAttacking || member.InCombat) && target is GameNPC)
                        activeTargets.Add(target);
                }

                if (member.ControlledBrain?.Body?.TargetObject is GameLiving petTarget && petTarget.IsAlive)
                {
                    selectedTargets.Add(petTarget);
                    if (petTarget is GameNPC)
                        activeTargets.Add(petTarget);
                }
            }
            if (activeTargets.Count == 0)
                return false;

            foreach (GameLiving expired in _shamanPveRootRetryUntil
                         .Where(pair => now >= pair.Value || pair.Key?.IsAlive != true)
                         .Select(pair => pair.Key).ToArray())
                _shamanPveRootRetryUntil.Remove(expired);

            List<GameNPC> nearby = shaman.GetNPCsInRadius(1800);
            HashSet<GameLiving> petTargets = GroupPetTargets(nearby, members);
            foreach (GameNPC add in nearby
                         .Where(npc => npc?.IsAlive == true && npc.Realm == eRealm.None &&
                             !BotPvpCrowdControl.PlayerLike(npc) && CanDefendAgainst(npc))
                         .OrderBy(shaman.GetDistanceTo).Take(24))
            {
                GameLiving victim = add.TargetObject as GameLiving;
                bool attacksMember = victim != null && members.Contains(victim);
                bool alreadyControlled = add.IsMezzed || add.IsStunned ||
                    add.effectListComponent.ContainsEffectForEffectType(eEffect.SnareImmunity);
                long retryUntil = _shamanPveRootRetryUntil.GetValueOrDefault(add);
                bool safeAdd = BardBotCrowdControlPolicy.IsSafePveAdd(true,
                    activeTargets.Any(target => target != add), attacksMember,
                    selectedTargets.Contains(add), Body.TargetObject == add,
                    add.HealthPercent, alreadyControlled, now, retryUntil) && IsUntouchedAdd(add, petTargets);
                if (!ShamanBotCombatPolicy.IsRootableAdd(safeAdd, victim == null ? 0 : add.GetDistanceTo(victim)))
                    continue;

                foreach (Spell spell in roots)
                {
                    if (shaman.Mana < shaman.PowerCost(spell) || shaman.GetSkillDisabledDuration(spell) > 0 ||
                        spell.CastTime > 0 && shaman.IsBeingInterrupted ||
                        !shaman.IsWithinRadius(add, spell.CalculateEffectiveRange(shaman)) ||
                        !NeedsOffensiveSpellApplication(add, spell) ||
                        !BotGroupSupport.HasCorpseLineOfSight(shaman, add))
                        continue;

                    if (_shamanPveRootRetryUntil.Count >= 32)
                    {
                        GameLiving oldest = _shamanPveRootRetryUntil
                            .OrderBy(pair => pair.Value).First().Key;
                        _shamanPveRootRetryUntil.Remove(oldest);
                    }
                    _shamanPveRootRetryUntil[add] = now + ShamanPveRootRetryMilliseconds;
                    GameObject previous = shaman.TargetObject;
                    shaman.TargetObject = add;
                    try
                    {
                        shaman.StopAttack();
                        shaman.StopMovingOnPath();
                        shaman.StopMoving();
                        return CheckOffensiveSpells(spell);
                    }
                    finally
                    {
                        shaman.TargetObject = previous;
                    }
                }
            }

            return false;
        }
    }
}
