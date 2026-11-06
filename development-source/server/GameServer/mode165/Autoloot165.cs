namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode's autoloot (owner 2026-11-05: "add an autoloot option like old repo had with a toggle on and off in
    /// settings (default off)"). 1.65 had no autoloot, so it is an option, off unless the player turns it on: the new
    /// client's setting sends "/autoloot on|off quiet" when you enter the world and when you change it. The choice lives for
    /// the session only, so the character's old saved autoloot flag (on by default in the database) never turns it on by
    /// itself. Gamebots and companions never autoloot: they walk to their bags (BotLootWalk165).
    /// </summary>
    public static class Autoloot165
    {
        private const string Key = "autoloot165";

        public static bool IsOn(GamePlayer player) => player?.TempProperties.GetProperty<bool>(Key) == true;

        public static void Set(GamePlayer player, bool on)
        {
            player.TempProperties.SetProperty(Key, on);
            player.Autoloot = on;   // the pick-up itself still reads the character's flag
        }

        /// <summary>Whether a loot owner picks its loot up at once: a player with autoloot on, or a group led by one.</summary>
        public static bool Wants(IGameStaticItemOwner owner) => owner switch
        {
            GamePlayer player => IsOn(player),
            Group group => IsOn(group.Leader),
            _ => false,
        };
    }
}
