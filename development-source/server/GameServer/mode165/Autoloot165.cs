namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode's autoloot (owner 2026-11-05: "add an autoloot option like old repo had with a toggle on and off in
    /// settings (default off)"). 1.65 had no autoloot, so it is an option, off unless the player turns it on: the new
    /// client's setting sends "/autoloot on|off quiet" when you enter the world and when you change it. The choice lives for
    /// the session only, so the character's old saved autoloot flag (on by default in the database) never turns it on by
    /// itself. Gamebots and companions never autoloot: they walk to their bags (BotLootWalk165). The one exception is a
    /// realm raid's high-end boss loot (RealmRaidLootOwner): it is shared out at the kill - a quarter straight to the
    /// players' bags, the rest to the raid's gamebots - as agreed with the owner (2026-10-31; in True 1.65 it never ran,
    /// because the loot step skipped every owner).
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

        /// <summary>Whether a loot owner picks its loot up at once: a player with autoloot on, a group led by one, or a
    /// realm raid sharing out its boss loot.</summary>
        public static bool Wants(IGameStaticItemOwner owner) => owner switch
        {
            GamePlayer player => IsOn(player),
            Group group => IsOn(group.Leader),
            RealmRaidLootOwner => true,
            _ => false,
        };
    }
}
