/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using DOL.Database;
using DOL.Language;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
	/// <summary>How a combine ended (the enchant helper reads it; the trade window ignores it).</summary>
	public enum CombineOutcome : byte { None = 0, Applied, Failed, Exploded, Refused }

	/// <summary>
	/// The materials of one combine (gems / dust / tincture), the lock that guards them and the partner who watches.
	/// The trade window passes its own items, lock and partner (so its combine is exactly as before); the enchant helper
	/// (CraftEnchant165) passes backpack gems with no window and no partner.
	/// </summary>
	public sealed class CombineMaterials
	{
		public ArrayList Items { get; }
		public Lock Lock { get; }
		public GamePlayer Partner { get; }
		public int Count => Items.Count;
		/// <summary>Set by ApplyMagicalEffect: applied, failed (overcharge) or exploded.</summary>
		public CombineOutcome Outcome { get; set; }

		public CombineMaterials(ArrayList items, Lock sync, GamePlayer partner)
		{
			Items = items;
			Lock = sync;
			Partner = partner;
		}

		/// <summary>The trade window's own items, lock and partner.</summary>
		public static CombineMaterials Of(ITradeWindow window) => new(window.TradeItems, window.Lock, window.Partner);
	}

	/// <summary>
	/// AdvancedCraftingSkill is the skill for alchemy and spellcrafting whitch add all combine system
	/// </summary>
	public abstract class AdvancedCraftingSkill : AbstractProfession
    {
		#region Classic craft function
		protected override bool CheckForTools(GamePlayer player, Recipe recipe)
		{
			foreach (GameStaticItem item in player.GetItemsInRadius(CRAFT_DISTANCE))
			{
				if (item.Name.ToLower() == "alchemy table" || item.Model == 820) // Alchemy Table
				{
					return true;
				}
			}

			player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Crafting.CheckTool.NotHaveTools", recipe.Product.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
			player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "Crafting.CheckTool.FindAlchemyTable"), eChatType.CT_System, eChatLoc.CL_SystemWindow);

			if (player.Client.Account.PrivLevel > 1)
				return true;

			return false;
		}

		#endregion

		#region Advanced craft function

		#region First call function
		
		/// <summary>
		/// Called when player accept to combine items
		/// </summary>
		/// <param name="player"></param>
		/// <returns></returns>
		public virtual bool CombineItems(GamePlayer player)
		{
			if(player.TradeWindow.PartnerTradeItems == null || player.TradeWindow.PartnerItemsCount != 1)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AdvancedCraftingSkill.CombineItems.OnlyCombine"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}

			DbInventoryItem itemToCombine = (DbInventoryItem)player.TradeWindow.PartnerTradeItems[0];
			CombineMaterials materials = CombineMaterials.Of(player.TradeWindow);
			if(!IsAllowedToCombine(player, itemToCombine, materials)) return false;

			ApplyMagicalEffect(player, itemToCombine, materials);

			return true;
		}

		/// <summary>
		/// The enchant helper's combine (CraftEnchant165): the same checks, rolls, messages and item saving as accepting
		/// the trade window, with the gems / tincture given from the player's own backpack and no window.
		/// </summary>
		public CombineOutcome CombineWithoutWindow(GamePlayer player, DbInventoryItem item, IEnumerable<DbInventoryItem> materials)
		{
			var set = new CombineMaterials(new ArrayList(new List<DbInventoryItem>(materials)), new Lock(), null);
			if (!IsAllowedToCombine(player, item, set))
				return CombineOutcome.Refused;
			ApplyMagicalEffect(player, item, set);
			return set.Outcome;
		}

		#endregion

		#region Requirement check

		/// <summary>
        /// Check if the player can enchant the item
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		public bool IsAllowedToCombine(GamePlayer player, DbInventoryItem item)
		{
			if(item == null) return false;
			return IsAllowedToCombine(player, item, CombineMaterials.Of(player.TradeWindow));
		}

		/// <summary>
        /// Check if the player can enchant the item with these materials (the trade window's or the enchant helper's)
		/// </summary>
		public virtual bool IsAllowedToCombine(GamePlayer player, DbInventoryItem item, CombineMaterials materials)
		{
			if(item == null) return false;

			if(materials.Count <= 0)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AdvancedCraftingSkill.IsAllowedToCombine.Imbue", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;	
			}

			if(!item.IsCrafted)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AdvancedCraftingSkill.IsAllowedToCombine.CraftedItems"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;
			}


            DbInventoryItem itemToCombine = (DbInventoryItem)materials.Items[0];

            if (itemToCombine.Object_Type == (int)eObjectType.AlchemyTincture)
            {
                if (item.Object_Type != (int)eObjectType.Instrument) // Only check for non instruments
                {
                    switch (itemToCombine.Type_Damage)
                    {
                        case 0: //Type damage 0 = armors
                            if (!GlobalConstants.IsArmor(item.Object_Type))
                            {
                                if (item.Object_Type == (int)eObjectType.Shield) // think shield can do armor and weapon ? not verified.
                                    return true;

                                player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AdvancedCraftingSkill.IsAllowedToCombine.NoGoodCombine"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                                return false;
                            }
                            break;
                        case 1: //Type damage 1 = weapons
                            if (!GlobalConstants.IsWeapon(item.Object_Type))
                            {
                                player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AdvancedCraftingSkill.IsAllowedToCombine.NoGoodCombine"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                                return false;
                            }
                            break;
                        default:
                            player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AdvancedCraftingSkill.IsAllowedToCombine.ProblemCombine"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                            return false;
                    }
                }
                else // Instrument
                {
                    if (itemToCombine.Type_Damage != 0) //think instrument can do only armorproc ? not verified.
                    {
                        player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AdvancedCraftingSkill.IsAllowedToCombine.NoGoodCombine"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
                        return false;
                    }
                }
            }

            if (!GlobalConstants.IsArmor(item.Object_Type) && !GlobalConstants.IsWeapon(item.Object_Type) && item.Object_Type != (int)eObjectType.Instrument)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "AdvancedCraftingSkill.IsAllowedToCombine.NoEnchanted"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
				return false;	
			}

			return true;
		}
		
		#endregion
		
		#region Apply magical effect

		/// <summary>
        /// Apply the magical bonus to the item
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		protected abstract void ApplyMagicalEffect(GamePlayer player, DbInventoryItem item, CombineMaterials materials);
		
		#endregion

		#endregion

	}
}
