namespace DOL.GS
{
    /// <summary>
    /// The launcher's two XP rate options (owner 2026-11-10: back from the old repo): Player XP rate (xp_rate) and
    /// Gamebot XP rate (bot_xp_rate). Both default to 1x. A hired companion earns at the player rate, as in the old
    /// repo (bot_xp_rate never applied to companion helpers), so it keeps pace with the player it follows.
    /// </summary>
    public static class XpRates165
    {
        /// <summary>A bad stored rate (NaN, infinite, zero or less) counts as the original 1x, as the launcher reads it
        /// (XpRateSettings.Parse; owner 2026-11-10).</summary>
        public static double Sanitize(double rate) => double.IsFinite(rate) && rate > 0 ? rate : 1.0;

        /// <summary>The player's kill XP multiplier: the RvR rate replaces the player rate in RvR zones.</summary>
        public static double PlayerMultiplier(bool isRvR, double playerRate, double rvrRate) =>
            isRvR ? Sanitize(rvrRate) : Sanitize(playerRate);

        /// <summary>
        /// A gamebot's kill XP multiplier. An autonomous bot uses the gamebot rate (times the RvR rate in RvR zones,
        /// as before); a hired companion uses exactly the player's multiplier.
        /// </summary>
        public static double BotMultiplier(bool hiredCompanion, bool isRvR, double playerRate, double botRate, double rvrRate) =>
            hiredCompanion
                ? PlayerMultiplier(isRvR, playerRate, rvrRate)
                : Sanitize(botRate) * (isRvR ? Sanitize(rvrRate) : 1.0);
    }
}
