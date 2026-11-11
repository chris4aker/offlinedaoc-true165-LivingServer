using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// /stuck (owner 2026-11-10, player issue #30): asks "are you sure" in the game's own dialog, then the character dies
    /// where it stands, so it can release to its bind stone or a city. A free teleport would be too convenient; this
    /// death alone costs no experience, Constitution or resurrection sickness (StuckDeath165) - every other death keeps its penalties.
    /// Not in combat, so it can never be used to dodge a death that is coming anyway.
    /// </summary>
    [CmdAttribute("&stuck",
        ePrivLevel.Player,
        "Stuck? Die where you stand (after a confirmation) to release to your bind stone or a city, with no experience loss",
        "/stuck")]
    public class StuckCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        private static readonly DOL.Logging.Logger log = DOL.Logging.LoggerManager.Create(typeof(StuckCommandHandler));

        public const string Question = "Are you sure? You will die where you stand so you can release to your bind stone or a city. " +
                                       "This death costs no experience, Constitution or resurrection sickness.";

        public void OnCommand(GameClient client, string[] args)
        {
            if (IsSpammingCommand(client.Player, "stuck"))
                return;

            GamePlayer player = client.Player;
            string refusal = Refusal(player.IsAlive, InFight(player));
            if (refusal != null)
            {
                DisplayMessage(client, refusal);
                return;
            }
            player.Out.SendCustomDialog(Question, Answered);
        }

        /// <summary>Pure rule: why /stuck is refused right now (null: it may go ahead).</summary>
        public static string Refusal(bool alive, bool inFight) =>
            !alive ? "You are already dead." :
            inFight ? "You can't use /stuck while you are in combat." : null;

        private static bool InFight(GamePlayer player) => player.InCombat || player.IsAttacking || player.InCombatPvP;

        private static void Answered(GamePlayer player, byte response)
        {
            if (response != 0x01 || player == null || player.ObjectState != GameObject.eObjectState.Active)
                return;
            string refusal = Refusal(player.IsAlive, InFight(player));   // the world may have moved on while it was asked
            if (refusal != null)
            {
                player.Out.SendMessage(refusal, eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            // The death is processed a tick later (GameLiving.Die only queues it), so the stamp stays until that death
            // reads it (StuckDeath165); every death removes it, and it only counts within 2 s with no killer.
            player.TempProperties.SetProperty(GamePlayer.StuckCommandDeathProperty, GameLoop.GameLoopTime);
            log.Info($"STUCK_COMMAND player={player.Name} level={player.Level} xp={player.Experience} deathCount={player.DeathCount} region={player.CurrentRegionID} x={player.X} y={player.Y}");
            player.TakeDamage(null, eDamageType.Natural, player.MaxHealth + player.Health, 0);
            // no death started (flying, immune...): nothing may be left for a later death
            if (player.IsAlive)
                player.TempProperties.RemoveProperty(GamePlayer.StuckCommandDeathProperty);
        }
    }
}
