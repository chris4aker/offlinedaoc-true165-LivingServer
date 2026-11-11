using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS.Commands
{
    [CmdAttribute("&defensive", ePrivLevel.Player, "Companions wait for enemies to approach you", "/defensive")]
    public class CompanionDefensiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            CompanionEngagementMode.Set(client.Player, true);
            if (client.Player.Group != null)
                foreach (GameBot bot in client.Player.Group.GetMembersInTheGroup().OfType<GameBot>())
                    if (CompanionEngagementMode.DefensiveLeader(bot) == client.Player && bot.Brain is BotBrain brain)
                        brain.EnforceCompanionEngagementRange();
            DisplayMessage(client, "Companions: DEFENSIVE. Pull enemies within 350 units of you; companions and their pets will engage nearby threats. Use /aggressive to restore normal assisting.");
        }
    }
    [CmdAttribute("&aggressive", ePrivLevel.Player, "Restore normal companion assisting (default)", "/aggressive")]
    public class CompanionAggressiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            CompanionEngagementMode.Set(client.Player, false);
            DisplayMessage(client, "Companions: AGGRESSIVE (default). They assist your attacks normally again.");
        }
    }
    /// <summary>
    /// Owner 2026-11-11: "for retreating or walking through areas without stopping to fight anything". Companions and
    /// their pets stop fighting, drop every target and follow; /aggressive or /defensive ends it.
    /// </summary>
    [CmdAttribute("&passive", ePrivLevel.Player, "Companions and their pets fight nothing and follow you", "/passive")]
    public class CompanionPassiveCommand : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client.Player;
            CompanionEngagementMode.Set(player, CompanionStance.Passive);
            if (PlayerCompanionGrind.IsActive(player))
                PlayerCompanionGrind.Stop(player, "companions passive");
            if (player.Group != null)
                foreach (GameBot bot in player.Group.GetMembersInTheGroup().OfType<GameBot>())
                    if (CompanionEngagementMode.PassiveLeader(bot) == player && bot.Brain is BotBrain brain)
                        brain.HoldCompanionPassive();
            DisplayMessage(client, "Companions: PASSIVE. Companions and their pets stop fighting, ignore enemies and follow you, even when attacked. Use /aggressive or /defensive to fight again.");
        }
    }
}
