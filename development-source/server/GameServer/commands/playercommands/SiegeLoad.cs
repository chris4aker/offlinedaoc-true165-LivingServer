using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>
    /// True 1.65 Mode /load (period siege commands /arm /load /aim /fire, FH 157111): with no ammunition in the siege
    /// weapon you control, one fitting piece goes from your backpack into it; then it loads the ammunition selected in
    /// the siege window (else the first). The period way, dragging a piece onto the engine and pressing LOAD, works too.
    /// </summary>
    [Cmd("&load", ePrivLevel.Player, "Loads ammunition into the siege weapon you control (True 1.65 Mode)", "/load")]
    public class SiegeLoadCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null || IsSpammingCommand(player, "load"))
                return;
            GameSiegeWeapon weapon = player.SiegeWeapon;
            if (weapon == null)
            {
                DisplayMessage(client, "You must control a siege weapon to load it.");
                return;
            }
            if (!SiegeAmmo165.UsesAmmo(weapon))
            {
                DisplayMessage(client, weapon is GameSiegeRam ? "A ram needs no ammunition." : "This siege weapon needs no ammunition.");
                return;
            }
            if (!weapon.IsWithinRadius(player, weapon.SIEGE_WEAPON_CONTROLE_DISTANCE))
            {
                player.Out.SendMessage(weapon.GetName(0, true) + " is too far away for you to control!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            weapon.LoadFromBackpack(player);
        }
    }
}
