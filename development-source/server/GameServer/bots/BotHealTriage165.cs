using System;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 heal triage. Prima (Healer tips): "Over-healing your party paints a target on you. Try to keep everyone at
    /// two-thirds Health or better, and stick to Minor Heals instead of large Heals." Freddyshouse healer threads
    /// (2002-2003): small heals by default, big and instant heals for emergencies, no group heal on the pull (it draws
    /// aggro from every monster), the main tank first. Normal mode keeps its own numbers.
    /// </summary>
    public static class BotHealTriage165
    {
        public const int EmergencyPercent = 40;
        /// <summary>Where every healer starts healing ("two-thirds Health"); the player's "Heal at" choice overrides it.</summary>
        public const int HealAtPercent = 65;
        /// <summary>Outside an emergency a tank is triaged this many points lower than its health.</summary>
        public const int TankBonusPercent = 10;
        public const int GroupHealMembers = 3;
        /// <summary>A group heal waits until the fight has run this long (not on the pull).</summary>
        public const long GroupHealFightMilliseconds = 4_000;

        /// <summary>The health a member is triaged at: a tank outside an emergency counts 10 points lower, never into one.</summary>
        public static int TriageHealth(int healthPercent, bool tank) =>
            tank && healthPercent >= EmergencyPercent
                ? Math.Max(EmergencyPercent, healthPercent - TankBonusPercent)
                : healthPercent;

        /// <summary>Whether the candidate is healed before the current pick (the lower triage health first).</summary>
        public static bool Prefer(int candidateHealth, bool candidateTank, int currentHealth, bool currentTank) =>
            TriageHealth(candidateHealth, candidateTank) < TriageHealth(currentHealth, currentTank);

        /// <summary>
        /// A group heal (not a group heal-over-time, which may still go on two): three or more members under the heal line,
        /// and in a fight only once it has run a few seconds.
        /// </summary>
        public static bool GroupHealWorthwhile(int membersHurt, bool inFight, long fightMilliseconds) =>
            membersHurt >= GroupHealMembers && (!inFight || fightMilliseconds >= GroupHealFightMilliseconds);

        /// <summary>A heal over time goes up on the tank once it is hurt below this, before it needs a real heal.</summary>
        public const int PreHotBelowPercent = 90;
        /// <summary>The healer keeps this much power before a heal over time nobody needs yet.</summary>
        public const int PreHotMinPowerPercent = 50;

        /// <summary>
        /// GitHub #28 review (owner 2026-11-10): a heal over time only on a hurt target with none running (from anyone)
        /// and none being cast at it by a groupmate: HoTs of a kind do not stack, and recasting overwrote the running one.
        /// </summary>
        public static bool HotWorthCasting(bool hotRunning, bool hotComing, int healthPercent) =>
            !hotRunning && !hotComing && healthPercent < 100;

        /// <summary>
        /// A veteran 1.65 healer puts a heal over time on the tank as the sustained damage starts (owner 2026-11-10):
        /// in a fight, the tank fighting and hurt but not yet at the heal line, none running, power to spare.
        /// Big direct heals stay for the spikes (the emergency branch).
        /// </summary>
        public static bool PreHotTank(bool groupInFight, bool tankFighting, int tankHealthPercent, bool hotRunningOrComing,
            int healerPowerPercent) =>
            groupInFight && tankFighting && tankHealthPercent < PreHotBelowPercent && tankHealthPercent >= EmergencyPercent &&
            !hotRunningOrComing && healerPowerPercent >= PreHotMinPowerPercent;

        /// <summary>
        /// Owner 2026-11-10 (review): only a heal over time of the same kind holds back a new one - any HealOverTime
        /// effect, the same spell or spell line, or one of the bot's own heal-over-time spells. Shaman regenerations are
        /// health regen buffs, the family of songs and other regen buffs: a song or pulse, or another line's regen buff,
        /// never blocks a real heal over time.
        /// </summary>
        public static bool SameKindHot(Spell hot, Spell running, bool runningIsOwnHot) =>
            hot != null && running != null && !running.IsPulsing &&
            (running.ID == hot.ID || runningIsOwnHot ||
             hot.SpellType == eSpellType.HealOverTime && running.SpellType == eSpellType.HealOverTime ||
             hot.Group != 0 && running.Group == hot.Group && running.SpellType == hot.SpellType);

        /// <summary>
        /// Owner 2026-11-10 (review): the pre-HoT on a tank above the heal line never moves or stops the healer. The tank
        /// must already be in the spell's range and line of sight (checked at the call), and a cast-time one goes only on
        /// a healer standing free: not swinging, walking or following.
        /// </summary>
        public static bool PreHotHealerFree(bool instant, bool healerAttacking, bool healerMoving, bool healerFollowing) =>
            instant || !healerAttacking && !healerMoving && !healerFollowing;

        /// <summary>The heal line for a healing class: the player's choice, else 65% in True 1.65, 80% in normal mode.</summary>
        public static int HealerNeedAt(int healAtChoice, bool classic165) =>
            healAtChoice == 1 ? 90 : healAtChoice == 2 ? 50 : classic165 ? HealAtPercent : 80;

        /// <summary>A tank: a tank-class bot or player (Armsman, Warrior, Hero ...).</summary>
        public static bool IsTank(GameLiving living) => living switch
        {
            GameBot bot => BotPartyRoles.IsTank(bot),
            GamePlayer player => player.CharacterClass != null &&
                                 BotPartyRoles.For((eCharacterClass)player.CharacterClass.ID) == BotPartyRole.Tank,
            _ => false,
        };
    }
}
