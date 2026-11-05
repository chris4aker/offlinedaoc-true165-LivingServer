using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Who sees a stealthed monster (owner 2026-11-05): a player or a bot within the monster's aggro range (at least 150);
    /// beyond that it is hidden until it attacks, which takes its stealth off. Players (GamePlayer.CanDetect) and bots
    /// (their monster searches) use the same rule, so a bot never pulls an ambusher a player could not have seen.
    /// </summary>
    public static class StealthedMonsters165
    {
        public const int MinimumSightRange = 150;

        public static int SightRange(GameNPC monster) =>
            System.Math.Max(MinimumSightRange, monster?.Brain is StandardMobBrain hider ? hider.AggroRange : 0);

        /// <summary>False only for a stealthed monster (not a gamebot) farther from the viewer than its sight range.</summary>
        public static bool CanSee(GameObject viewer, GameNPC monster) =>
            monster == null || !monster.IsStealthed || monster is GameBot ||
            viewer != null && viewer.IsWithinRadius(monster, SightRange(monster));
    }
}
