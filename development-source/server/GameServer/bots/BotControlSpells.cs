using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// Crowd control and snares used like a veteran player (owner 2026-11-08). Mez and root only go on adds nobody is
    /// attacking, never on the kill target; a snare only on a runner; a stun only to interrupt a cast or to peel a monster
    /// off a non-tank; a long-reuse stun or mez only in an emergency. Snare-nukes (damage + snare) count as damage.
    /// These spells are kept out of the ordinary nuke rotation and only go through the dedicated rules.
    /// </summary>
    public static class BotControlSpells
    {
        public enum ControlKind
        {
            None,
            Mez,
            Root,
            Snare,
            Stun,
            Nearsight,
        }

        /// <summary>A monster under this health that moves away from its fight is a runner.</summary>
        public const int RunnerHealthPercent = 35;
        /// <summary>A reuse of a minute or more (Paralyzing Glance, Hypnotic Harmony) is an emergency spell.</summary>
        public const int LongReuseMilliseconds = 60_000;
        /// <summary>A plain nuke doing at least this share of the snare-nuke's damage is "similar power".</summary>
        public const double PlainNukeDamageShare = 0.8;

        /// <summary>The kind of control a spell's own type gives (no bomber payload lookup).</summary>
        public static ControlKind KindOfType(Spell spell) => spell?.SpellType switch
        {
            eSpellType.Mesmerize or eSpellType.Mez => ControlKind.Mez,
            eSpellType.Stun => ControlKind.Stun,
            eSpellType.Nearsight => ControlKind.Nearsight,
            eSpellType.SpeedDecrease or eSpellType.UnbreakableSpeedDecrease =>
                spell.Value >= 99 ? ControlKind.Root : ControlKind.Snare,
            _ => ControlKind.None,
        };

        /// <summary>The kind of control a spell gives; an Animist bomber is classified by its payload.</summary>
        public static ControlKind KindWith(Spell spell, Func<int, Spell> resolve)
        {
            if (spell?.SpellType == eSpellType.Bomber)
                return spell.SubSpellID > 0 && resolve != null ? KindOfType(resolve(spell.SubSpellID)) : ControlKind.None;
            return KindOfType(spell);
        }

        private sealed class KindBox
        {
            public readonly ControlKind Kind;
            public KindBox(ControlKind kind) => Kind = kind;
        }

        // the bomber payload kind cached on each bomber Spell (thread-safe, dies with the Spell)
        private static readonly ConditionalWeakTable<Spell, KindBox> BomberKinds = new();

        /// <summary>The kind of control a spell gives, using the loaded spell table for bomber payloads.</summary>
        public static ControlKind Kind(Spell spell)
        {
            if (spell?.SpellType != eSpellType.Bomber)
                return KindOfType(spell);
            if (spell.SubSpellID <= 0)
                return ControlKind.None;
            if (BomberKinds.TryGetValue(spell, out KindBox box))
                return box.Kind;
            return BomberKinds.GetValue(spell,
                bomber => new KindBox(KindOfType(SkillBase.GetSpellByID(bomber.SubSpellID)))).Kind;
        }

        public static bool IsControl(Spell spell) => Kind(spell) != ControlKind.None;
        public static bool IsControlWith(Spell spell, Func<int, Spell> resolve) => KindWith(spell, resolve) != ControlKind.None;

        /// <summary>A bomber carrying a control payload (Animist Vines, Constraining..Binding Spirit roots).</summary>
        public static bool IsControlBomber(Spell spell) =>
            spell?.SpellType == eSpellType.Bomber && Kind(spell) != ControlKind.None;

        public static bool IsControlBomberWith(Spell spell, Func<int, Spell> resolve) =>
            spell?.SpellType == eSpellType.Bomber && KindWith(spell, resolve) != ControlKind.None;

        public static bool IsStun(Spell spell) => spell?.SpellType == eSpellType.Stun;

        public static bool IsLongReuse(Spell spell) =>
            spell != null && spell.HasRecastDelay && spell.RecastDelay >= LongReuseMilliseconds;

        /// <summary>
        /// Pure rule: control on the bot's own (attacked) target. Against a player-like enemy anything goes (PvP keeps
        /// its control). Against a monster: never a mez, root or nearsight; a snare only on a runner; a stun only when
        /// the stun rule says so.
        /// </summary>
        public static bool AllowsOnOwnTarget(ControlKind kind, bool playerLikeTarget, bool runner, bool stunWorthwhile) =>
            kind switch
            {
                ControlKind.None => true,
                _ when playerLikeTarget => true,
                ControlKind.Snare => runner,
                ControlKind.Stun => stunWorthwhile,
                _ => false,
            };

        /// <summary>Pure rule: a runner moves, is low on health and is not in melee with whoever it fights.</summary>
        public static bool IsRunner(bool moving, int healthPercent, bool inMeleeWithVictim) =>
            moving && healthPercent < RunnerHealthPercent && !inMeleeWithVictim;

        /// <summary>
        /// Pure rule: a stun interrupts a cast or peels a monster off a non-tank, never filler; a long-reuse stun only in
        /// an emergency (three or more adds, see BotBrain.AllowsLongReuseControl).
        /// </summary>
        public static bool StunWorthwhile(bool targetCasting, bool peelsNonTank, bool longReuse, bool emergency) =>
            (targetCasting || peelsNonTank) && (!longReuse || emergency);

        /// <summary>
        /// Pure rule: an add is safe to mez or root only when nobody touched it: nobody really attacking it, no damage
        /// over time or snare on it, not this bot's own target and not targeted by any groupmate's pet or elemental.
        /// </summary>
        public static bool IsUntouchedAdd(bool realAttacker, bool hasDamageOrSnareEffect, bool ownTarget,
            bool targetedByGroupPet) =>
            !realAttacker && !hasDamageOrSnareEffect && !ownTarget && !targetedByGroupPet;

        /// <summary>
        /// Pure rule: whether the add's recent attackers (its attacker tracker, a few seconds) include a real one. The
        /// add's own victim counts only when it is attacking the add back: a damage shield's hits come from the victim.
        /// </summary>
        public static bool HasRealAttacker<T>(IEnumerable<T> attackers, T victim, bool victimAttacksAdd) where T : class
        {
            if (attackers == null)
                return false;
            foreach (T attacker in attackers)
                if (attacker != null && (!ReferenceEquals(attacker, victim) || victimAttacksAdd))
                    return true;
            return false;
        }

        /// <summary>
        /// Rule: while a caster holds its damage for the tank only real debuffs and an interrupt/peel stun stay; snares,
        /// roots, mez and nearsight go too, except against a player-like enemy (PvP keeps its control).
        /// </summary>
        public static bool HoldDamageStrips(Spell spell, bool playerLikeTarget) =>
            BotCasterPriority.IsDamage(spell) || !playerLikeTarget && IsControl(spell) && !IsStun(spell);

        /// <summary>
        /// Pure rule: with a groupmate (or pet) holding the target, a plain single-target nuke of similar power is cast
        /// instead of a snare-nuke (the snare is wasted on a held monster).
        /// </summary>
        public static bool PrefersPlainNuke(Spell snareNuke, Spell plain, bool groupHoldsTarget) =>
            groupHoldsTarget && snareNuke?.SpellType == eSpellType.DamageSpeedDecrease && plain != null &&
            plain.SpellType is eSpellType.DirectDamage or eSpellType.Bolt && plain.Radius <= 0 && snareNuke.Radius <= 0 &&
            plain.Damage >= snareNuke.Damage * PlainNukeDamageShare;
    }
}
