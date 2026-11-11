namespace DOL.GS
{
    /// <summary>
    /// Owner 2026-11-10: a gamebot party walking with no player had bots snapping to face a party member and straight
    /// back, over and over. The buff upkeep (BotBrain.TryMaintainTravelAndClassBuffs) ran while the party walked: a member
    /// out of buff range made the bot WalkTo that member (it turned to them) until the formation follow turned it back a
    /// moment later, every second; a member in range made it stop (ending its formation follow) for a cast that
    /// GameBot.CastSpell then refused (AutonomousGroupFormationFollow.DeferBuff). Out-of-combat upkeep now waits until
    /// the party stands still. Heals and fights are not held.
    /// </summary>
    public static class BotTravelCastHold
    {
        /// <summary>
        /// The party counts as travelling this long after its leader's last real step (MovementComponent.LastWalkTick).
        /// Owner 2026-11-10 (review): longer than the slowest travel AI turn (5 s plus stagger at 1,500 bots), so the
        /// leader's pause between path legs never counts as standing still.
        /// </summary>
        public const long SettleMilliseconds = 6_000;

        /// <summary>
        /// Pure: an autonomous bot party (no player) holds its out-of-combat upkeep while its leader or the bot itself
        /// walks, or the leader walked within <see cref="SettleMilliseconds"/>. A known stop (the bot or its leader
        /// sitting or resting) ends that settle at once. Never while anyone in it fights.
        /// </summary>
        public static bool Holds(bool autonomousParty, bool anyoneFighting, bool walking, bool restingStop,
            long millisecondsSinceLeaderWalked) =>
            autonomousParty && !anyoneFighting &&
            (walking || !restingStop && millisecondsSinceLeaderWalked >= 0 && millisecondsSinceLeaderWalked < SettleMilliseconds);
    }
}
