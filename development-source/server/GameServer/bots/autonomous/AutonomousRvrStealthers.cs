namespace DOL.GS;

/// <summary>
/// True 1.65 Mode stealthers: assassins (Infiltrator, Shadowblade, Nightshade) and most archers (Scout, Hunter, Ranger)
/// hunt alone or now and then in pairs instead of joining warbands, travel hidden in the frontier, sit at the period
/// hunting posts (the milegates and outside the enemy portal keeps, AutonomousFrontierHotspots) and pick the easy kill
/// first: someone sitting, then a caster low on power, then someone hurt. Minstrels stay group singers.
/// </summary>
public static class AutonomousRvrStealthers
{
    // A stealther's hunting post is this many times as likely a roaming pick as a keep's surroundings.
    public const int PostWeight = 40;

    /// <summary>Every assassin; three archers in four (the rest group with a warband).</summary>
    public static bool PrefersSolo(eCharacterClass characterClass, long botId) =>
        BotPoisonSupply.IsAssassin(characterClass) ||
        BotRangedCombat.IsDedicatedArcher(characterClass) && Mix(botId) % 4 != 0;

    public static bool PrefersSolo(GameBot bot) =>
        bot?.CharacterClass != null && PrefersSolo((eCharacterClass)bot.CharacterClass.ID, bot.DatabaseID);

    /// <summary>About one stealther in five looks for another stealther to pair up with rather than hunt alone.</summary>
    public static bool FormsDuo(long botId) => Mix(botId) / 4 % 10 < 2;

    public static bool TravelsHidden(bool classicMode, bool prefersSolo, bool hasStealthSpec, bool inFrontier, bool safeArea,
        bool busy, bool mounted, int groupSize) =>
        classicMode && prefersSolo && hasStealthSpec && inFrontier && !safeArea && !busy && !mounted && groupSize <= 2;

    /// <summary>0 sitting, 1 a caster under 30% power, 2 under 50% health, 3 anyone else.</summary>
    public static int Vulnerability(bool sitting, bool hasPower, int manaPercent, int healthPercent) =>
        sitting ? 0 : hasPower && manaPercent < 30 ? 1 : healthPercent < 50 ? 2 : 3;

    public static int Vulnerability(GameLiving target) => target == null ? 3 :
        Vulnerability(target.IsSitting, target.MaxMana > 0, target.ManaPercent, target.HealthPercent);

    private static ulong Mix(long id) => unchecked((ulong)id * 0x9E3779B97F4A7C15UL) >> 33;
}
