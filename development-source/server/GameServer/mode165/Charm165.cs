using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode charming for bots, gamebots and hired companions alike (owner 2026-10-30, plan section 25): no
    /// made-up pets. A bot charms a real monster, and only with a charm spell it has really learned from its own spec
    /// lines (some classes' charm is deep in an advanced line). It charms the first valid target it meets in combat that
    /// cons at least green: the right creature type for the spell, within the spell's level limit, nobody's pet.
    /// </summary>
    public static class Charm165
    {
        /// <summary>The best charm spell the bot has learned (none: it fights without one).</summary>
        public static Spell LearnedCharm(GameBot bot) =>
            bot.Spells?.Where(s => s != null && s.SpellType == eSpellType.Charm && s.Level <= bot.Level)
                .OrderByDescending(s => s.Level).FirstOrDefault();

        /// <summary>Whether this monster can be charmed by this bot with this spell, under the 1.65 rules.</summary>
        public static bool IsValidTarget(GameBot bot, Spell charm, GameNPC mob)
        {
            if (charm == null || mob == null || !mob.IsAlive || mob.ObjectState != GameObject.eObjectState.Active ||
                mob.CurrentRegion != bot.CurrentRegion || mob.Realm != eRealm.None || mob.BodyType is < 1 or > 11 ||
                mob.Brain is IControlledBrain || mob is GameSummonedPet or GameEpicBoss or GameEpicNPC or GameBot ||
                !AutonomousPetSupport.IsCharmBodyTypeAllowed(charm, mob.BodyType))
                return false;
            if (ServerProperties.Properties.SPELL_CHARM_NAMED_CHECK != 0 && !string.IsNullOrEmpty(mob.Name) && char.IsUpper(mob.Name[0]))
                return false;
            // the original Dawn of Light limit, as for players: the spell's maximum level and its share of the caster's level
            if (CharmSpellHandler.TooStrong165(charm, bot.Level, mob.Level))
                return false;
            return ConLevels.GetConColor(bot.GetConLevel(mob)) >= ConColor.GREEN;
        }

        /// <summary>
        /// Whether anyone else in the charmer's group is on this monster: hurt it already, a member other than the
        /// charmer targets or attacks it, or a group member's pet (a Theurgist's elementals, a bot's charmed pet) was
        /// sent at it. Such a monster is the group's fight and is never charmed away from it.
        /// </summary>
        public static bool GroupIsOnIt(GameBot charmer, HashSet<GameLiving> members, GameNPC mob)
        {
            if (mob.HealthPercent < 100)
                return true;
            bool Ours(GameLiving l) =>
                l != null && l != charmer && (members.Contains(l) ||
                    l is GameNPC { Brain: IControlledBrain cb } && (members.Contains(cb.Owner) || members.Contains(cb.GetPlayerOwner())));
            if (members.Any(m => m != charmer && m.TargetObject == mob))
                return true;
            if (mob.attackComponent?.AttackerTracker?.Attackers.Any(Ours) == true)
                return true;
            return mob.GetNPCsInRadius(3000).Any(n => n.TargetObject == mob && Ours(n));
        }

        /// <summary>A pulsing charm of this caster is running (a Minstrel's charm song, a Mentalist's charm), even
        /// between a broken charm and its recapture.</summary>
        public static bool HoldsPulsingCharm(GameLiving caster)
        {
            List<ECSPulseEffect> pulses = caster?.effectListComponent?.GetPulseEffects();
            return pulses != null && pulses.Any(p => p?.SpellHandler?.Spell?.SpellType == eSpellType.Charm);
        }

        /// <summary>
        /// The pet this caster holds with a pulsing charm (song or spell), the charm spell that holds it, and how long
        /// the hold lasts without another pulse (each pulse renews it; a song switched away from leaves it running out).
        /// </summary>
        public static GameNPC PulsingCharmPet(GameLiving caster, out Spell charm, out long holdLeftMs)
        {
            charm = null;
            holdLeftMs = 0;
            if (caster?.ControlledBrain?.Body is not GameNPC pet || !pet.IsAlive)
                return null;
            ECSGameSpellEffect hold = EffectListService.GetSpellEffectOnTarget(pet, eEffect.Charm, eSpellType.Charm);
            if (hold == null || hold.IsEnding || hold.SpellHandler?.Caster != caster || hold.SpellHandler.Spell is not { Pulse: not 0 } spell)
                return null;
            charm = spell;
            holdLeftMs = System.Math.Max(0, hold.ExpireTick - GameLoop.GameLoopTime);
            return pet;
        }

        /// <summary>
        /// Whether this pulsing spell would cost the caster its charmed pet: while the charm pulses, any other pulsing
        /// spell or song ends it, and casting the charm again stops it; between a twisted song and the renewed charm,
        /// only the charm itself may start. The one partner song of a deliberate twist is let through by its caller.
        /// </summary>
        public static bool PulseWouldLosePet(GameLiving caster, Spell spell)
        {
            if (spell == null || spell.Pulse == 0)
                return false;
            if (HoldsPulsingCharm(caster))
                return true;
            return spell.SpellType != eSpellType.Charm && PulsingCharmPet(caster, out _, out _) != null;
        }

        /// <summary>No bot tries to charm the same monster again within this time (no charm loops).</summary>
        public const int RetryMilliseconds = 60_000;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameNPC, object> Tried = new();

        /// <summary>
        /// With no pet of any kind (a Hunter with its wolf never charms: owner 2026-10-30), cast the learned charm on a
        /// valid monster (at least green, the right kind, within the spell's level limit). Hired companions seek one
        /// whenever their group fights; gamebots only charm a monster fighting them alone, never the group's or raid's
        /// target. Never in a realm raid, and no bot retries a monster any bot tried in the last minute, so bots can't
        /// take turns charming and stall a fight.
        /// </summary>
        public static bool TryCharm(GameBot bot, GameLiving combatTarget, out string activity)
        {
            activity = null;
            if (!Classic165Mode.IsOn || bot == null || bot.ControlledBrain != null ||
                bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true || AutonomousRealmRaid.IsReserved(bot))
                return false;
            // owner 2026-11-05 (reset-day S33): a hired companion whose player switched charming off never charms; a
            // Minstrel's pet support passes no fight target (its pet is defensive only), so its own target in a fight is used
            if (!CompanionOptions165.CharmAllowed(bot))
                return false;
            combatTarget ??= bot.InCombat ? bot.TargetObject as GameLiving : null;
            Spell charm = LearnedCharm(bot);
            // a charm song still pulsing (between a break and its recapture) is left to run: casting it again would stop it
            if (charm == null || bot.Mana < bot.PowerCost(charm) || bot.GetSkillDisabledDuration(charm) > 0 || HoldsPulsingCharm(bot))
                return false;
            long now = GameLoop.GameLoopTime;
            bool Fresh(GameNPC m) => !Tried.TryGetValue(m, out object last) || now - (long)last >= RetryMilliseconds;
            GameNPC mob;
            if (HiredCompanions165.IsHired(bot))
            {
                // A hired companion seeks a charm whenever its group fights (owner 2026-10-30): its own target first,
                // then any monster fighting the group, the first valid one (at least green, the right kind, in range).
                // Only an add nobody else is on: never the monster the group is fighting (GitHub #13, 2026-11-03: a
                // Sorcerer charmed the mob its owner's Theurgist pet had been sent at, and the fight was gone).
                if (bot.Group == null)
                    return false;
                var members = bot.Group.GetMembersInTheGroup().ToHashSet();
                IEnumerable<GameNPC> candidates = (combatTarget is GameNPC own ? new[] { own } : [])
                    .Concat(bot.GetNPCsInRadius((ushort)System.Math.Min(charm.Range > 0 ? charm.Range : 1000, 1500))
                        .Where(n => n.InCombat && n.TargetObject is GameLiving t && members.Contains(t)));
                // the charm that fits each monster (a Hunter's animal or insect charm - owner 2026-11-06)
                mob = candidates.FirstOrDefault(m => Fresh(m) && CharmFor(bot, m) != null && !GroupIsOnIt(bot, members, m));
                if (mob == null)
                    return false;
                charm = CharmFor(bot, mob);
                if (bot.Mana < bot.PowerCost(charm) || bot.GetSkillDisabledDuration(charm) > 0)
                    return false;
            }
            else
            {
                // A gamebot charms only a monster fighting it alone: never the group's or raid's target.
                mob = combatTarget as GameNPC;
                if (mob == null || !Fresh(mob) || !IsValidTarget(bot, charm, mob) ||
                    mob.attackComponent?.AttackerTracker?.Attackers.Any(a => a != bot && a is not GameSummonedPet) == true)
                    return false;
            }
            if (mob == null)
                return false;
            // the Minstrel's charm is a song: it needs the instrument in hand (in a fight the bot holds its weapon)
            if (charm.NeedInstrument && !BotBrain.TryEquipRealInstrument(bot, charm.InstrumentRequirement))
                return false;
            Tried.AddOrUpdate(mob, now);
            GameObject previous = bot.TargetObject;
            bot.TargetObject = mob;
            bool started = bot.CastSpell(charm, SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells), true);
            if (!started)
                bot.TargetObject = previous;
            else
                activity = $"Charming {mob.Name}";
            return started;
        }

        // ---- hired companions only (owner 2026-11-06): the pre-fight charm, /charm, and the charm that fits a monster

        /// <summary>
        /// The best charm this bot has learned that can hold this monster under the 1.65 rules (its kind, its level limit,
        /// at least green to the bot), or null. A Hunter has an animal and an insect charm line; this picks the right one.
        /// </summary>
        public static Spell CharmFor(GameBot bot, GameNPC mob) =>
            bot.Spells?.Where(s => s != null && s.SpellType == eSpellType.Charm && s.Level <= bot.Level && IsValidTarget(bot, s, mob))
                .OrderByDescending(s => s.Level).FirstOrDefault();

        /// <summary>A hired companion whose charming is on and that knows a charm (the AI Options charm row).</summary>
        public static bool CompanionCharms(GameBot bot) =>
            bot != null && HiredCompanions165.IsHired(bot) && CompanionOptions165.CharmAllowed(bot) && LearnedCharm(bot) != null;

        /// <summary>Pure: nobody has started on this monster: full health, not in a fight, nobody hitting it.</summary>
        public static bool Untouched(int healthPercent, bool inCombat, bool attacked) => healthPercent >= 100 && !inCombat && !attacked;

        /// <summary>
        /// The pre-fight charm (owner 2026-11-06): a hired companion with charming on and no pet charms the monster its
        /// player has selected, before anyone starts on it - at least green to the companion, the right kind, within its
        /// charm's level limit, in range. Tried once a minute per monster, like the in-fight charm.
        /// </summary>
        public static bool TryPreFightCharm(GameBot bot)
        {
            if (!Classic165Mode.IsOn || !CompanionCharms(bot) || !bot.IsAlive || bot.ControlledBrain != null || bot.InCombat ||
                bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true || HoldsPulsingCharm(bot))
                return false;
            GamePlayer leader = bot.PlayerGroupLeader;
            if (leader?.TargetObject is not GameNPC mob || leader.Group == null || leader.Group != bot.Group || leader.IsAttacking ||
                !Untouched(mob.HealthPercent, mob.InCombat, mob.attackComponent?.AttackerTracker?.Attackers.Any() == true))
                return false;
            if (Tried.TryGetValue(mob, out object last) && GameLoop.GameLoopTime - (long)last < RetryMilliseconds)
                return false;
            Spell charm = CharmFor(bot, mob);
            if (charm == null || !bot.IsWithinRadius(mob, charm.Range > 0 ? charm.Range : 1000))
                return false;
            return Start(bot, mob, charm) == null;
        }

        /// <summary>
        /// "/charm" or "/charm (companion name)" (owner 2026-11-06): that companion (or the first one with a charm) charms
        /// the player's target now. A pet it already has is let go first - a summoned pet dismissed, a charmed one released,
        /// a pulsing charm stopped - then it charms the new one. The target rules still hold (at least green to it, the right
        /// kind, within its charm's level limit). Returns the reply for the player.
        /// </summary>
        public static string Order(GamePlayer player, GameLiving target, string name)
        {
            if (target is not GameNPC mob || !mob.IsAlive)
                return "Select a monster first, then type /charm, or /charm <companion name>.";
            GameBot[] companions = player.Group?.GetMembersInTheGroup().OfType<GameBot>()
                .Where(b => HiredCompanions165.IsHired(b) && b.IsAlive && b.PlayerGroupLeader == player).ToArray() ?? [];
            GameBot bot;
            if (!string.IsNullOrWhiteSpace(name))
            {
                bot = PlayerLedPullCoordinator.FindNamed(companions, name.Trim());
                if (bot == null)
                    return $"No companion named {name.Trim()} is with you.";
                if (LearnedCharm(bot) == null)
                    return $"{bot.Name} knows no charm.";
            }
            else
            {
                bot = companions.FirstOrDefault(b => LearnedCharm(b) != null);
                if (bot == null)
                    return "None of your companions knows a charm.";
            }
            if (bot.IsCasting)
                bot.StopCurrentSpellcast();
            Spell charm = CharmFor(bot, mob);
            if (charm == null)
                return $"{bot.Name} can't charm {mob.GetName(0, false)}: {TargetProblem(bot, mob)}.";
            if (!bot.IsWithinRadius(mob, charm.Range > 0 ? charm.Range : 1000))
                return $"{mob.GetName(0, true)} is too far away for {bot.Name} to charm.";
            ReleasePet(bot);
            string problem = Start(bot, mob, charm);
            return problem ?? $"{bot.Name} tries to charm {mob.GetName(0, false)}.";
        }

        /// <summary>Why no learned charm of this bot can hold this monster (the first rule it fails).</summary>
        private static string TargetProblem(GameBot bot, GameNPC mob)
        {
            Spell[] charms = bot.Spells?.Where(s => s != null && s.SpellType == eSpellType.Charm && s.Level <= bot.Level).ToArray() ?? [];
            if (mob.Realm != eRealm.None || mob.Brain is IControlledBrain || mob is GameSummonedPet or GameEpicBoss or GameEpicNPC or GameBot ||
                ServerProperties.Properties.SPELL_CHARM_NAMED_CHECK != 0 && !string.IsNullOrEmpty(mob.Name) && char.IsUpper(mob.Name[0]))
                return "it can't be charmed";
            if (ConLevels.GetConColor(bot.GetConLevel(mob)) < ConColor.GREEN)
                return "it is grey to them";
            if (!charms.Any(s => AutonomousPetSupport.IsCharmBodyTypeAllowed(s, mob.BodyType)))
                return "their charm can't hold that kind of creature";
            return "it is too strong for their charm";
        }

        /// <summary>Lets the bot's current pet go: a pulsing charm stopped, then the pet released (dismissed or freed).</summary>
        private static void ReleasePet(GameBot bot)
        {
            foreach (ECSPulseEffect pulse in bot.effectListComponent?.GetPulseEffects()
                         .Where(p => !p.IsEnding && !p.IsEnded && p.SpellHandler?.Spell?.SpellType == eSpellType.Charm).ToList() ?? [])
                pulse.End();
            if (bot.ControlledBrain != null)
                bot.CommandNpcRelease();
        }

        /// <summary>Casts this charm on the monster; null when the cast started, else why not.</summary>
        private static string Start(GameBot bot, GameNPC mob, Spell charm)
        {
            if (bot.Mana < bot.PowerCost(charm))
                return $"{bot.Name} doesn't have the power to charm.";
            if (bot.GetSkillDisabledDuration(charm) > 0)
                return $"{bot.Name}'s charm isn't ready yet.";
            // the Minstrel's charm is a song: it needs the instrument in hand
            if (charm.NeedInstrument && !BotBrain.TryEquipRealInstrument(bot, charm.InstrumentRequirement))
                return $"{bot.Name} has no instrument for that song.";
            bot.WakeRecoveryRest();
            Tried.AddOrUpdate(mob, GameLoop.GameLoopTime);
            GameObject previous = bot.TargetObject;
            bot.TargetObject = mob;
            if (bot.CastSpell(charm, SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells), true))
                return null;
            bot.TargetObject = previous;
            return $"{bot.Name} couldn't start the charm.";
        }
    }
}
