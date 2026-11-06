namespace DOL.GS
{
    /// <summary>
    /// The pet focus damage shield of Cabalists (Matter Manipulation), Enchanters (Empowering) and Spiritmasters (Spirit
    /// Enhancement): a 2.5 s cast, then a channel that pulses on the pet for power while the caster stands still. It was
    /// disabled for bots because a channelling bot stood in place for good: the engine queued every later spell behind the
    /// channel and the bot's turn saw itself "casting". Now a bot's next spell ends its channel, as a player's does
    /// (CastingComponent), and the shield is used the way a player uses it: on a pet that is being hit (tanking, as when
    /// soloing; never while a real tank holds the monster, owner 2026-10-28) in a fight that will last, with
    /// nothing on the caster, no enemy players about and power to save (between 20% and 60%; above that the caster nukes,
    /// or when it has nothing else to cast). The channel is dropped when the pet's fight ends or the caster is hit, and
    /// gives way to heals and add control. After a broken channel the caster waits 10 s before trying again.
    /// True 1.65 opener (period Cabalist play): in the first 10 s of a fight on the pet the shield goes up at any power
    /// from 20%, so the pet takes and holds the monster; with power above 60% that channel is broken after 8 s for nukes.
    /// </summary>
    public static class BotPetFocusShieldPolicy
    {
        public const int MinPowerPercent = 20;
        public const int MaxPowerPercent = 60;
        public const int KeepPowerPercent = 5;
        public const int MinTargetHealthPercent = 40;
        public const long BackoffMilliseconds = 10_000;
        public const long MaxChannelMilliseconds = 120_000;
        public const long OpenerMilliseconds = 10_000;
        public const long OpenerChannelMilliseconds = 8_000;

        public static bool IsPetFocusShield(Spell spell) =>
            spell?.SpellType == eSpellType.DamageShield && spell.IsFocus && spell.Target == eSpellTarget.PET;

        public static bool ShouldStart(bool petInMelee, int targetHealthPercent, bool casterUnderAttack, bool enemyPlayersNear,
            bool mustMove, int powerPercent, bool nothingElseToCast, bool petShielded, long now, long backoffUntil,
            bool opener = false) =>
            petInMelee && !petShielded && !casterUnderAttack && !enemyPlayersNear && !mustMove && now >= backoffUntil &&
            targetHealthPercent >= MinTargetHealthPercent && powerPercent >= MinPowerPercent &&
            (powerPercent <= MaxPowerPercent || nothingElseToCast || opener);

        /// <summary>True 1.65: the fight on the pet is young enough for the shield as an opener.</summary>
        public static bool InOpenerWindow(long petTankedMilliseconds) =>
            petTankedMilliseconds >= 0 && petTankedMilliseconds <= OpenerMilliseconds;

        /// <summary>An opener channel started with power to spare has done its job: break it and nuke.</summary>
        public static bool OpenerDone(bool openerChannel, int powerPercent, long channelMilliseconds) =>
            openerChannel && powerPercent > MaxPowerPercent && channelMilliseconds >= OpenerChannelMilliseconds;

        public static bool ShouldKeep(bool petInMelee, bool casterUnderAttack, bool mustMove, int powerPercent, long channelMilliseconds) =>
            petInMelee && !casterUnderAttack && !mustMove && powerPercent >= KeepPowerPercent &&
            channelMilliseconds < MaxChannelMilliseconds;
    }
}
