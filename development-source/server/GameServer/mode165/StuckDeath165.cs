namespace DOL.GS
{
    /// <summary>
    /// /stuck as a chosen death (owner 2026-11-10, player issue #30): /stuck stamps the time, then kills the player. The
    /// death itself is processed a tick later (GameLiving.Die only queues it with the ReaperService), so the stamp has to
    /// outlive the command and is consumed by the next death that is processed. Only a death within
    /// <see cref="WindowMilliseconds"/> of the stamp with no killer counts as the /stuck death; it costs no experience, no
    /// Constitution and no resurrection sickness and does not count toward the next death's penalty. Every other death is
    /// paid in full, and a stamp is never kept past the next death, so it can't spare a later one.
    /// </summary>
    public static class StuckDeath165
    {
        public const long WindowMilliseconds = 2_000;

        /// <summary>Pure rule: is this death the one /stuck asked for?</summary>
        public static bool IsStuckDeath(long stampTick, long nowTick, bool killerIsNull) =>
            stampTick > 0 && killerIsNull && nowTick >= stampTick && nowTick - stampTick <= WindowMilliseconds;
    }
}
