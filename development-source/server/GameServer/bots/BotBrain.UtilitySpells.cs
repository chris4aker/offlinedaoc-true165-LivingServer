using System.Linq;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Spells bots never cast before the 2026-11-05 class audit, used only where period players used them (owner: "if
    /// there was a real player period use for them in combat"):
    /// - Life Transfer (Cabalist): an emergency heal for a groupmate (Prima: "perform emergency heals on your realm
    ///   buddies ... watch your healer's status"); only while the Cabalist itself is healthy.
    /// - Amnesia (Sorcerer, Bard's Lullaby): a monster that turned on the caster forgets it, so the pet takes the anger back
    ///   (Prima, Sorcerer: "cast Amnesia on it and let your pet make a few attacks"); in RvR it interrupts a casting enemy
    ///   ("can interrupt casters and archers").
    /// - Confusion: only on an enemy player's pet ("does not work on PCs, but it DOES work on their pets").
    /// - Nearsight: the first spell on an enemy caster or archer in RvR (SI Prima: "the first spell you cast on a caster or
    ///   Scout"; Runemasters "Nearsight their opponent and nuke").
    /// Cure Nearsight is in CheckHeals. A hired companion's AI Options can switch Life Transfer and Amnesia off.
    /// </summary>
    public partial class BotBrain
    {
        private const int LifeTransferPatientPercent = 35;
        private const int LifeTransferOwnHealthPercent = 60;
        private long _nextUtilitySpellLook;

        private Spell ReadySpell(eSpellType type) =>
            Body.Spells?.Where(spell => spell != null && spell.SpellType == type && spell.Level <= Body.Level &&
                                        Body.GetSkillDisabledDuration(spell) <= 0 && Body.Mana >= BotBody.PowerCost(spell))
                .OrderByDescending(spell => spell.Level)
                .FirstOrDefault();

        private bool CastOn(Spell spell, GameLiving target)
        {
            if (spell == null || target == null || !Body.IsWithinRadius(target, System.Math.Max(spell.Range, spell.Radius)))
                return false;
            GameObject previous = Body.TargetObject;
            Body.TargetObject = target;
            bool cast = Body.CastSpell(spell, m_mobSpellLine);
            if (!cast || spell.IsInstantCast)
                Body.TargetObject = previous;
            return cast;
        }

        private bool TryUtilitySpells()
        {
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (bot == null || !Classic165Mode.IsOn || now < _nextUtilitySpellLook || bot.IsCasting ||
                bot.castingComponent?.HasPendingSkillRequests == true || bot.IsIncapacitated)
                return false;
            _nextUtilitySpellLook = now + 1_000;
            return TryLifeTransfer() || TryAmnesiaForPet() || TryPvpUtility();
        }

        private bool TryLifeTransfer()
        {
            if (Body.Group == null || Body.HealthPercent < LifeTransferOwnHealthPercent ||
                CompanionOptions165.Choice(BotBody, "lifetransfer") == 1 || ReadySpell(eSpellType.LifeTransfer) is not Spell transfer)
                return false;
            GameLiving patient = Body.Group.GetMembersInTheGroup()
                .Where(member => member != Body && member.IsAlive && member.CurrentRegion == Body.CurrentRegion &&
                                 member.HealthPercent < LifeTransferPatientPercent && Body.IsWithinRadius(member, transfer.Range))
                .OrderBy(member => member.HealthPercent)
                .FirstOrDefault();
            return patient != null && CastOn(transfer, patient);
        }

        private bool TryAmnesiaForPet()
        {
            if (CompanionOptions165.Choice(BotBody, "amnesia") == 1 ||
                Body.ControlledBrain?.Body is not GameNPC pet || !pet.IsAlive || ReadySpell(eSpellType.Amnesia) is not Spell amnesia)
                return false;
            // a monster on the caster that the pet is near enough to take back
            GameNPC onMe = Body.GetNPCsInRadius(1_000)
                .FirstOrDefault(npc => npc.IsAlive && npc is not GameBot && npc.Realm == eRealm.None && npc.TargetObject == Body &&
                                       npc.InCombat && pet.IsWithinRadius(npc, 600));
            return onMe != null && CastOn(amnesia, onMe);
        }

        private bool TryPvpUtility()
        {
            if (Body.TargetObject is not GameLiving target || !target.IsAlive ||
                !GameServer.ServerRules.IsAllowedToAttack(Body, target, true))
                return false;
            // Confusion on an enemy player's pet
            if (target is GameNPC { Brain: IControlledBrain { Owner: GameLiving petOwner } } &&
                petOwner is GamePlayer or GameBot && petOwner.Realm != Body.Realm &&
                ReadySpell(eSpellType.Confusion) is Spell confusion && !LivingHasEffect(target, confusion))
                return CastOn(confusion, target);
            if (target is not (GamePlayer or GameBot) || target.Realm == Body.Realm)
                return false;
            // Amnesia interrupts a casting enemy
            if (target.IsCasting && CompanionOptions165.Choice(BotBody, "amnesia") != 1 &&
                ReadySpell(eSpellType.Amnesia) is Spell interrupt)
                return CastOn(interrupt, target);
            // Nearsight first on an enemy caster or archer
            ICharacterClass enemyClass = target switch { GamePlayer p => p.CharacterClass, GameBot b => b.CharacterClass, _ => null };
            bool casterOrArcher = enemyClass != null && (enemyClass.ClassType == eClassType.ListCaster ||
                                  BotRangedCombat.IsDedicatedArcher((eCharacterClass)enemyClass.ID));
            if (casterOrArcher && !target.effectListComponent.ContainsEffectForEffectType(eEffect.Nearsight) &&
                ReadySpell(eSpellType.Nearsight) is Spell nearsight)
                return CastOn(nearsight, target);
            return false;
        }
    }
}
