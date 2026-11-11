using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// The AI Options that choose which attack spells a hired companion casts (owner 2026-11-11, CompanionSpellOptions165):
    /// Role "Melee and heal, no attack spells", Save power "Strict" and "Attack spells" (area only / none). One gate for
    /// every offensive cast path (CanCastOffensiveSpell, CheckInstantOffensiveSpells, PointBlankReady); the ordered or
    /// /grind pull spell has its own path and is not gated. Gamebots read choice 0 everywhere and never reach a block.
    /// </summary>
    public partial class BotBrain
    {
        private long _nextSpellOptionReport;

        /// <summary>True when the player's AI Options rule this spell out for this companion now.</summary>
        private bool SpellOptionBlocks(Spell spell)
        {
            GameBot bot = BotBody;
            if (spell == null || bot?.CharacterClass == null || HiredCompanions165.HiredRecord(bot) == null)
                return false;
            string reason = null;
            if (CompanionSpellOptions165.MeleeHealRole(bot) && CompanionSpellOptions165.MeleeHealBlocks(spell))
                reason = "melee-heal";
            else if (CompanionOptions165.Choice(bot, "attackspells") is int attack and > 0 &&
                     CompanionSpellOptions165.AttackChoiceBlocks(attack, spell))
                reason = attack == CompanionSpellOptions165.AttackNone ? "attack-none" : "attack-area-only";
            else if (BotCasterPriority.IsDamage(spell) &&
                     CompanionOptions165.Choice(bot, "savepower") == CompanionSpellOptions165.SavePowerStrict &&
                     CompanionSpellOptions165.StrictBlocks(spell, SavePowerForGroup()))
                reason = "savepower-strict";
            if (reason == null)
                return false;
            long now = GameLoop.GameLoopTime;
            if (now >= _nextSpellOptionReport)
            {
                _nextSpellOptionReport = now + 60_000;
                log.Info($"COMPANION_SPELL_OPTION reason={reason} bot=\"{bot.Name}\" class=\"{bot.ClassName}\" " +
                         $"spell=\"{spell.Name}\" power={Body.ManaPercent}% region={Body.CurrentRegionID}");
            }
            return true;
        }

        private long _nextFightBladeturnReport;

        /// <summary>
        /// "Attack spells: None" (a Runemaster's "afk personal bladeturn"): a used-up self bladeturn (Runic Ward) goes back
        /// up in the fight too, whenever nothing has hit the caster for 3 s - the general upkeep waits for the fight's end.
        /// </summary>
        private bool TryNoAttackSpellsBladeturn()
        {
            if (!CastsNoAttackSpells || !TryRestoreSelfBladeturn(true))
                return false;
            long now = GameLoop.GameLoopTime;
            if (now >= _nextFightBladeturnReport)
            {
                _nextFightBladeturnReport = now + 60_000;
                log.Info($"COMPANION_FIGHT_BLADETURN bot=\"{BotBody.Name}\" class=\"{BotBody.ClassName}\" " +
                         $"power={Body.ManaPercent}% region={Body.CurrentRegionID}");
            }
            return true;
        }

        /// <summary>A hired caster whose player chose "Attack spells: None - buffs only".</summary>
        private bool CastsNoAttackSpells =>
            BotBody != null && CompanionOptions165.Choice(BotBody, "attackspells") == CompanionSpellOptions165.AttackNone;
    }
}
