using System;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode: the housing-market taxidermist (Elkyrd in Caerwent, Bruhdor in Erikstaad, Urlaun in Meath) who
    /// cleanses a Darkness Falls Daemon Diamond Seal into a cleansed diamond seal, the spellcraft ingredient of crushed
    /// diamond seal dust and the etched aquamarine eye (Allakhazam item 9417, Oct 2003; owner 2026-11-06: "add it").
    /// Hand him seals: every seal handed over comes back cleansed (no fee is recorded anywhere). Replaces a quest-based
    /// hand-in, which would have taken a whole stack for one cleansed seal.
    /// </summary>
    public class Taxidermist165 : GameNPC
    {
        public const string SealId = "DiamondSeal";
        public const string CleansedSealId = "cleansed_diamond_seal";

        public static bool IsSeal(DbInventoryItem item) =>
            item != null && string.Equals(item.Id_nb, SealId, StringComparison.OrdinalIgnoreCase);

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;
            TurnTo(player, 5000);
            SayTo(player, eChatLoc.CL_PopupWindow,
                "Bring me a Daemon Diamond Seal from Darkness Falls and I will cleanse it for you. Hand them to me one at a time, or the whole stack.");
            return true;
        }

        public override bool ReceiveItem(GameLiving source, DbInventoryItem item)
        {
            if (source is not GamePlayer player || item == null)
                return false;
            if (!IsSeal(item))
            {
                SayTo(player, eChatLoc.CL_PopupWindow, "I only cleanse Daemon Diamond Seals.");
                return false;
            }
            DbItemTemplate cleansed = GameServer.Database.FindObjectByKey<DbItemTemplate>(CleansedSealId);
            if (cleansed == null)
                return false;
            // the whole stack handed over is cleansed; the seals come back if the cleansed ones don't fit
            int count = Math.Max(1, item.Count);
            if (!player.Inventory.RemoveCountFromStack(item, count))
                return false;
            if (!player.Inventory.AddTemplate(GameInventoryItem.Create(cleansed), count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
            {
                DbItemTemplate seal = GameServer.Database.FindObjectByKey<DbItemTemplate>(item.Id_nb);
                if (seal != null)
                    player.Inventory.AddTemplate(GameInventoryItem.Create(seal), count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                SayTo(player, eChatLoc.CL_PopupWindow, "Your pack is full; make some room first.");
                return false;
            }
            SayTo(player, eChatLoc.CL_PopupWindow, count == 1
                ? "There you are: a cleansed diamond seal."
                : $"There you are: {count} cleansed diamond seals.");
            return true;
        }
    }
}
