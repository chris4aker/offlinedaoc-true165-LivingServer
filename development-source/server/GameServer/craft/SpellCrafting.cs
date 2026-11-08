using System;
using System.Collections;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS
{
    public class SpellCrafting : AdvancedCraftingSkill
	{
        protected override String Profession
        {
            get
            {
                return "CraftersProfession.Spellcrafter";
            }
        }

        private static readonly int[,] itemMaxBonusLevel =  // taken from mythic Spellcraft calculator
		{
			{0,1,1,1,1,1,1},
			{1,1,1,1,1,2,2},
			{1,1,1,2,2,2,2},
			{1,1,2,2,2,3,3},
			{1,2,2,2,3,3,4},
			{1,2,2,3,3,4,4},
			{2,2,3,3,4,4,5},
			{2,3,3,4,4,5,5},
			{2,3,3,4,5,5,6},
			{2,3,4,4,5,6,7},
			{2,3,4,5,6,6,7},
			{3,4,4,5,6,7,8},
			{3,4,5,6,6,7,9},
			{3,4,5,6,7,8,9},
			{3,4,5,6,7,8,10},
			{3,5,6,7,8,9,10},
			{4,5,6,7,8,10,11},
			{4,5,6,8,9,10,12},
			{4,6,7,8,9,11,12},
			{4,6,7,8,10,11,13},
			{4,6,7,9,10,12,13},
			{5,6,8,9,11,12,14},
			{5,7,8,10,11,13,15},
			{5,7,9,10,12,13,15},
			{5,7,9,10,12,14,16},
			{5,8,9,11,12,14,16},
			{6,8,10,11,13,15,17},
			{6,8,10,12,13,15,18},
			{6,8,10,12,14,16,18},
			{6,9,11,12,14,16,19},
			{6,9,11,13,15,17,20},
			{7,9,11,13,15,17,20},
			{7,10,12,14,16,18,21},
			{7,10,12,14,16,19,21},
			{7,10,12,14,17,19,22},
			{7,10,13,15,17,20,23},
			{8,11,13,15,17,20,23},
			{8,11,13,16,18,21,24},
			{8,11,14,16,18,21,24},
			{8,11,14,16,19,22,25},
			{8,12,14,17,19,22,26},
			{9,12,15,17,20,23,26},
			{9,12,15,18,20,23,27},
			{9,13,15,18,21,24,27},
			{9,13,16,18,21,24,28},
			{9,13,16,19,22,25,29},
			{10,13,16,19,22,25,29},
			{10,14,17,20,23,26,30},
			{10,14,17,20,23,27,31},
			{10,14,17,20,23,27,31},
			{10,15,18,21,24,28,32},
		};

        private static readonly int[] OCStartPercentages = { 0, 10, 20, 30, 50, 70 };
        private static readonly int[] ItemQualOCModifiers = { 0, 0, 6, 8, 10, 18, 26 };

		public SpellCrafting()
		{
			Icon = 0x0D;
			Name = LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, 
				"Crafting.Name.Evocation");
			eSkill = eCraftingSkill.SpellCrafting;
		}

		#region Classic craft functions
		public override void GainCraftingSkillPoints(GamePlayer player, Recipe recipe)
		{
			if (Util.Chance(CalculateChanceToGainPoint(player, recipe.Level)))
			{
				player.GainCraftingSkill(eCraftingSkill.SpellCrafting, 1);

				if (player.GetCraftingSkillValue(eCraftingSkill.GemCutting) < subSkillCap)
	                player.GainCraftingSkill(eCraftingSkill.GemCutting, 1);

				player.Out.SendUpdateCraftingSkills();
			}
		}

		#endregion

		#region Requirement check

		/// <summary>
		/// This function is called when player accept the combine
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		public override bool IsAllowedToCombine(GamePlayer player, DbInventoryItem item, CombineMaterials materials)
		{
			if (!base.IsAllowedToCombine(player, item, materials)) return false;

			if (((DbInventoryItem)materials.Items[0]).Model == 525) // first ingredient to combine is Dust => echantement
			{
				if (item.Level < 15)
				{
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, 
						"SpellCrafting.IsAllowedToCombine.NoEnchanted"), eChatType.CT_System, 
						eChatLoc.CL_SystemWindow);

					return false;
				}

				lock (materials.Lock)
				{
					foreach (DbInventoryItem materialToCombine in materials.Items)
					{
						if (materialToCombine.Model != 525)
						{
							player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, 
								"SpellCrafting.IsAllowedToCombine.FalseMaterial", 
								materialToCombine.Name), 
								eChatType.CT_System, eChatLoc.CL_SystemWindow);

							return false;
						}
					}
				}
			}
			else
			{
				ArrayList bonusToApply = new ArrayList(4);
				if (item.Bonus1Type != 0)
				{
					bonusToApply.Add(item.Bonus1Type);
					if (item.Bonus2Type != 0)
					{
						bonusToApply.Add(item.Bonus2Type);
						if (item.Bonus3Type != 0)
						{
							bonusToApply.Add(item.Bonus3Type);
							if (item.Bonus4Type != 0)
							{
								player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.AlreadyImbued", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
								return false;
							}
						}
					}
				}

				lock (materials.Lock)
				{
					for (int i = 0; i < materials.Count; i++)
					{
						DbInventoryItem currentItem = (DbInventoryItem)materials.Items[i];

						if (currentItem.Object_Type != (int)eObjectType.SpellcraftGem)
						{
							player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.FalseItem"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
							return false;
						}

						if (bonusToApply.Contains(currentItem.Bonus1Type))
						{
							player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.NoSameBonus"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
							return false;
						}

						if (bonusToApply.Count > 4)
						{
							player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.DifferentTypes", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
							return false;
						}

						bonusToApply.Add(currentItem.Bonus1Type);
					}
				}

				int bonusLevel = GetTotalImbuePoints(player, item, materials);
				if (bonusLevel > player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting) / 20)
				{
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.NotEnoughSkill"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return false;
				}

				if (bonusLevel - GetItemMaxImbuePoints(item) > 6)
				{
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.NoMoreLevels", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return false;
				}
			}

			return true;
		}

		#endregion

		#region Apply magical effect

		/// <summary>
		/// Apply all needed magical bonus to the item
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		protected override void ApplyMagicalEffect(GamePlayer player, DbInventoryItem item, CombineMaterials materials)
		{
			if (item == null || materials.Items[0] == null) return; // be sure at least one item in each side

			if (((DbInventoryItem)materials.Items[0]).Model == 525) // Echant item
			{
				ApplyMagicalDusts(player, item, materials);
			}
			else if (((DbInventoryItem)materials.Items[0]).Object_Type == (int)eObjectType.SpellcraftGem) // Spellcraft item
			{
				ApplySpellcraftGems(player, item, materials);
			}

			if (item.Template is DbItemUnique)
			{
				GameInventoryObjectExtensions.SaveItem(item);
				GameServer.Database.SaveObject(item.Template as DbItemUnique);
			}
			else
			{
				ChatUtil.SendErrorMessage(player, "Spellcrafting error: Item was not an ItemUnique, crafting changes not saved to DB!");
				log.ErrorFormat("Spellcrafting error: Item {0} was not an ItemUnique for player {1}, crafting changes not saved to DB!", item.Id_nb, player.Name);
			}
		}

		/// <summary>
		/// Apply all magical dust
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		private void ApplyMagicalDusts(GamePlayer player, DbInventoryItem item, CombineMaterials materials)
		{
			int spellCrafterLevel = player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting);

			int bonusCap;
			if (spellCrafterLevel < 300 || item.Level < 15) bonusCap = 0;
			else if (spellCrafterLevel < 400 || item.Level < 20) bonusCap = 5;
			else if (spellCrafterLevel < 500 || item.Level < 25) bonusCap = 10;
			else if (spellCrafterLevel < 600 || item.Level < 30) bonusCap = 15;
			else if (spellCrafterLevel < 700 || item.Level < 35) bonusCap = 20;
			else if (spellCrafterLevel < 800 || item.Level < 40) bonusCap = 25;
			else if (spellCrafterLevel < 900 || item.Level < 45) bonusCap = 30;
			else bonusCap = 35;

			player.Inventory.BeginChanges();

			int bonusMod = item.Bonus;
			lock (materials.Lock)
			{
				foreach (DbInventoryItem gemme in materials.Items)
				{
					switch (gemme.Name)
					{
						case "coppery imbued dust": bonusMod += 2; break;
						case "coppery enchanted dust": bonusMod += 4; break;
						case "coppery glowing dust": bonusMod += 6; break;
						case "coppery ensorcelled dust": bonusMod += 8; break;

						case "silvery imbued dust": bonusMod += 10; break;
						case "silvery enchanted dust": bonusMod += 12; break;
						case "silvery glowing dust": bonusMod += 14; break;
						case "silvery ensorcelled dust": bonusMod += 16; break;

						case "golden imbued dust": bonusMod += 18; break;
						case "golden enchanted dust": bonusMod += 20; break;
						case "golden glowing dust": bonusMod += 22; break;
						case "golden ensorcelled dust": bonusMod += 24; break;

						case "mithril imbued dust": bonusMod += 26; break;
						case "mithril enchanted dust": bonusMod += 28; break;
						case "mithril glowing dust": bonusMod += 30; break;
						case "mithril ensorcelled dust": bonusMod += 32; break;

						case "platinum imbued dust": bonusMod += 34; break;
						case "platinum enchanted dust": bonusMod += 36; break;
						case "platinum glowing dust": bonusMod += 38; break;
						case "platinum ensorcelled dust": bonusMod += 40; break;

					}
					player.Inventory.RemoveCountFromStack(gemme, 1);
					InventoryLogging.LogInventoryAction(player, "(craft)", eInventoryActionType.Craft, gemme.Template);
				}
			}

			item.Bonus = Math.Min(bonusMod, bonusCap);

			player.Inventory.CommitChanges();
			materials.Outcome = CombineOutcome.Applied;
		}

		/// <summary>
		/// Apply all spellcraft gems bonus
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		private void ApplySpellcraftGems(GamePlayer player, DbInventoryItem item, CombineMaterials materials)
		{
			int maxBonusLevel = GetItemMaxImbuePoints(item);
			int bonusLevel = GetTotalImbuePoints(player, item, materials);

			int sucessChances = 100;
			if (bonusLevel > maxBonusLevel) sucessChances = CalculateChanceToOverchargeItem(player, item, maxBonusLevel, bonusLevel);
			int destroyChance = 100 - CalculateChanceToPreserveItem(player, item, maxBonusLevel, bonusLevel, materials);


			GamePlayer tradePartner = materials.Partner;

			if (Util.Chance(sucessChances))
			{
				materials.Outcome = CombineOutcome.Applied;
				lock (materials.Lock)
				{
					player.Inventory.BeginChanges();
					foreach (DbInventoryItem gem in (ArrayList)materials.Items.Clone())
					{
						if (item.Bonus1Type == 0)
						{
							item.Bonus1Type = gem.Bonus1Type;
							item.Bonus1 = gem.Bonus1;
						}
						else if (item.Bonus2Type == 0)
						{
							item.Bonus2Type = gem.Bonus1Type;
							item.Bonus2 = gem.Bonus1;
						}
						else if (item.Bonus3Type == 0)
						{
							item.Bonus3Type = gem.Bonus1Type;
							item.Bonus3 = gem.Bonus1;
						}
						else if (item.Bonus4Type == 0)
						{
							item.Bonus4Type = gem.Bonus1Type;
							item.Bonus4 = gem.Bonus1;
						}
						player.Inventory.RemoveCountFromStack(gem, 1);
						InventoryLogging.LogInventoryAction(player, "(craft)", eInventoryActionType.Craft, gem.Template);
					}
					player.Inventory.CommitChanges();
				}

				if (tradePartner != null)
				{
					tradePartner.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.ImbuedItem", player.Name), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				}
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.ImbuedItem", player.Name), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			}
			else if (Util.Chance(destroyChance))
			{
				materials.Outcome = CombineOutcome.Exploded;
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.PowerExplodes"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);

				lock (materials.Lock)
				{
					player.Inventory.BeginChanges();
                    // Luhz Crafting Update:
                    // The base item is no longer lost when spellcrafting explodes - only gems are destroyed.
                    foreach (DbInventoryItem gem in (ArrayList)materials.Items.Clone())
                    {
                        if (gem.Object_Type == (int)eObjectType.SpellcraftGem)
						{
                            player.Inventory.RemoveCountFromStack(gem, 1);
							InventoryLogging.LogInventoryAction(player, "(craft)", eInventoryActionType.Craft, gem.Template);
						}
                    }
                    player.Inventory.CommitChanges();
				}

				player.Emote(eEmote.SpellGoBoom);
				player.Health = 0;
				player.Die(player); // On official you take damages
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);

				if (tradePartner != null)
				{
					if (Util.Chance(40))
					{
						tradePartner.Emote(eEmote.SpellGoBoom);
						tradePartner.Health = 0;
						tradePartner.Die(player);
					}
					tradePartner.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				}
			}
			else
			{
				materials.Outcome = CombineOutcome.Failed;
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				if (tradePartner != null)
				{
					tradePartner.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				}
			}
		}

		#endregion

		# region Show informations to player

		public virtual void ShowSpellCraftingInfo(GamePlayer player, DbInventoryItem item)
		{
			// 525 == dust.
			if ((player.TradeWindow.TradeItems[0] as DbInventoryItem).Model == 525)
				return;

			ArrayList spellCraftingInfo = new(10);
			int totalItemCharges = GetItemMaxImbuePoints(item);
			int totalGemCharges = GetTotalImbuePoints(player, item);
			spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.TotalImbue", item.Name, totalItemCharges));
			spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.CurrentBonus", item.Name));

			if (item.Bonus1Type != 0)
			{
				spellCraftingInfo.Add($"* {SkillBase.GetPropertyName((eProperty) item.Bonus1Type)}: {item.Bonus1} {(item.Bonus1Type is >= (int) eProperty.Resist_First and <= (int) eProperty.Resist_Last ? "%" : " pts")}");

				if (item.Bonus2Type != 0)
				{
					spellCraftingInfo.Add($"* {SkillBase.GetPropertyName((eProperty) item.Bonus2Type)}: {item.Bonus2} {(item.Bonus2Type is >= (int) eProperty.Resist_First and <= (int) eProperty.Resist_Last ? "%" : "pts")}");

					if (item.Bonus3Type != 0)
					{
						spellCraftingInfo.Add($"* {SkillBase.GetPropertyName((eProperty) item.Bonus3Type)}: {item.Bonus3} {(item.Bonus3Type is >= (int) eProperty.Resist_First and <= (int) eProperty.Resist_Last ? "%" : "pts")}");

						if (item.Bonus4Type != 0)
							spellCraftingInfo.Add($"* {SkillBase.GetPropertyName((eProperty) item.Bonus4Type)}: {item.Bonus4} {(item.Bonus4Type is >= (int) eProperty.Resist_First and <= (int) eProperty.Resist_Last ? "%" : "pts")}");
					}
				}
			}

			spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.GemBonuses"));

			lock (player.TradeWindow.Lock)
			{
				for (int i = 0; i < player.TradeWindow.ItemsCount; i++)
				{
					DbInventoryItem currentGem = player.TradeWindow.TradeItems[i] as DbInventoryItem;
					spellCraftingInfo.Add($"* {currentGem.Name} - {SkillBase.GetPropertyName((eProperty) currentGem.Bonus1Type)}: ({GetGemImbuePoints(currentGem.Bonus1Type, currentGem.Bonus1)}) {currentGem.Bonus1} {(currentGem.Bonus1Type >= (int) eProperty.Resist_First && currentGem.Bonus1Type <= (int) eProperty.Resist_Last ? "%" : "pts")}");
				}
			}

			spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.ImbueCapacity", totalGemCharges, totalItemCharges));

			if (totalGemCharges > totalItemCharges)
			{
				int maxBonusLevel = GetItemMaxImbuePoints(item);
				int bonusLevel = GetTotalImbuePoints(player, item);
				spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.Penalty", GetOverchargePenality(totalItemCharges, totalGemCharges)));
				spellCraftingInfo.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfo.Success", CalculateChanceToOverchargeItem(player, item, maxBonusLevel, bonusLevel)));
			}

			GamePlayer partner = player.TradeWindow.Partner;

			foreach (string line in spellCraftingInfo)
			{
				player.Out.SendMessage(line, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				partner?.Out.SendMessage(line, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			}

			if (totalGemCharges > totalItemCharges)
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfos.Modified"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
		}

		# endregion

		#region Calcul functions

		/// <summary>
        /// Get the sucess chance to overcharge the item
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <param name="maxBonusLevel"></param>
		/// <param name="bonusLevel"></param>
		/// <returns></returns>
		protected int CalculateChanceToOverchargeItem(GamePlayer player, DbInventoryItem item, int maxBonusLevel, int bonusLevel)
		{
			return OverchargeSuccessChance(item.Quality, player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting), maxBonusLevel, bonusLevel);
		}

		/// <summary>CalculateChanceToOverchargeItem for an item quality and spellcrafting skill (percent).</summary>
		public static int OverchargeSuccessChance(int itemQuality, int spellcraftSkill, int maxBonusLevel, int bonusLevel)
		{
            // Luhz Crafting Update:
            // Overcharge success rate is now dependent only on item quality, points overcharged, and spellcrafter skill.
            // This formula is taken from Kort's Spellcrafting Calculator, which appears to be correct (for 1.87). Much Thanks!
            if(bonusLevel - maxBonusLevel > 5) return 0;
            if(bonusLevel - maxBonusLevel < 0) return 100;

            try {
            int success = 34 + ItemQualOCModifiers[itemQuality - 94];
            success -= OCStartPercentages[bonusLevel-maxBonusLevel];
            int skillbonus = (spellcraftSkill / 10);
            if (skillbonus > 100) 
                skillbonus = 100;
            success += skillbonus;
            int fudgefactor = (int)(100.0 * ((skillbonus / 100.0 - 1.0) * (OCStartPercentages[bonusLevel-maxBonusLevel] / 200.0)));
            success += fudgefactor;
            if (success > 100)
                success = 100;
            return success;
            }
            catch(Exception)
            {
                // Array access exception: someone is trying to imbue 100 < item.Quality < 94
                // Probably a GM or someone testing; I suppose we should just let them.
                return 100;
            }

            
		}

		/// <summary>
		/// Get the maximum bonus level the item can hold
		/// </summary>
		/// <param name="item"></param>
		/// <returns></returns>
		protected int GetItemMaxImbuePoints(DbInventoryItem item)
		{
			return ItemMaxImbuePoints(item.Level, item.Quality);
		}

		/// <summary>GetItemMaxImbuePoints for an item level and quality.</summary>
		public static int ItemMaxImbuePoints(int level, int quality)
		{
			//if (item.Level > 51) return 32;
			if (level < 1) return 0;
            // Luhz Crafting Update:
            // All items have MP level imbue points
            //return itemMaxBonusLevel[item.Level - 1, 100 - 94];
            
            if (quality == 100)
	            return (int)Math.Round(0.625 * level);
            if (quality == 99)
	            return (int)Math.Round(0.55 * level);
            if (quality == 98)
	            return (int)Math.Round(0.475 * level);
            if (quality == 97)
	            return (int)Math.Round(0.42 * level);
            if (quality == 96)
	            return (int)Math.Round(0.353 * level);
            if (quality == 95)
	            return (int)Math.Round(0.295 * level);
            
            return (int)Math.Round(0.2 * level);
            
		}

		/// <summary>
		/// Get gems bonus level
		/// </summary>
		/// <param name="player">player spellcrafting</param>
		/// <param name="item">item to calculate imbues for</param>
		/// <returns></returns>
		protected int GetTotalImbuePoints(GamePlayer player, DbInventoryItem item)
		{
			return GetTotalImbuePoints(player, item, CombineMaterials.Of(player.TradeWindow));
		}

		/// <summary>Get gems bonus level for these materials (the trade window's or the enchant helper's)</summary>
		protected int GetTotalImbuePoints(GamePlayer player, DbInventoryItem item, CombineMaterials materials)
		{
			var gems = new System.Collections.Generic.List<(int Type, int Value)>();
			lock (materials.Lock)
			{
				for (int i = 0; i < materials.Items.Count; i++)
				{
					DbInventoryItem currentGem = (DbInventoryItem)materials.Items[i];
					gems.Add((currentGem.Bonus1Type, currentGem.Bonus1));
				}
			}
			return TotalImbuePoints(item, gems);
		}

		/// <summary>
		/// The imbue points of the item's bonuses plus these gems: (sum of all + the biggest one) / 2. The item's own
		/// bonuses count as GetTotalImbuePoints always counted them: Bonus1-3 in order (an item with Bonus4 can't be imbued).
		/// </summary>
		public static int TotalImbuePoints(DbInventoryItem item, System.Collections.Generic.IEnumerable<(int Type, int Value)> gems)
		{
			int totalGemBonus = 0;
			int biggerGemCharge = 0;

			if (item.Bonus1Type != 0)
			{
				int currentBonusImbuePoint = GemImbuePoints(item.Bonus1Type, item.Bonus1);
				totalGemBonus += currentBonusImbuePoint;
				if (currentBonusImbuePoint > biggerGemCharge) biggerGemCharge = currentBonusImbuePoint;

				if (item.Bonus2Type != 0)
				{
					currentBonusImbuePoint = GemImbuePoints(item.Bonus2Type, item.Bonus2);
					totalGemBonus += currentBonusImbuePoint;
					if (currentBonusImbuePoint > biggerGemCharge) biggerGemCharge = currentBonusImbuePoint;

					if (item.Bonus3Type != 0)
					{
						currentBonusImbuePoint = GemImbuePoints(item.Bonus3Type, item.Bonus3);
						totalGemBonus += currentBonusImbuePoint;
						if (currentBonusImbuePoint > biggerGemCharge) biggerGemCharge = currentBonusImbuePoint;
					}
				}
			}

			foreach ((int gemType, int gemValue) in gems)
			{
				int currentGemCharge = GemImbuePoints(gemType, gemValue);
				if (currentGemCharge > biggerGemCharge) biggerGemCharge = currentGemCharge;

				totalGemBonus += currentGemCharge;
			}
			totalGemBonus += biggerGemCharge;
			totalGemBonus /= 2;

			return (int)Math.Floor((double)totalGemBonus);
		}

		/// <summary>
		/// Get how much the gem use like bonus point
		/// </summary>
		/// <param name="bonusType"></param>
		/// <param name="bonusValue"></param>
		/// <returns></returns>
		protected int GetGemImbuePoints(int bonusType, int bonusValue)
		{
			return GemImbuePoints(bonusType, bonusValue);
		}

		/// <summary>GetGemImbuePoints: the imbue points one bonus (gem) of this type and value uses.</summary>
		public static int GemImbuePoints(int bonusType, int bonusValue)
		{
			int gemBonus;
			if (bonusType <= (int)eProperty.Stat_Last) gemBonus = (int)(((bonusValue - 1) * 2 / 3) + 1); //stat
			else if (bonusType == (int)eProperty.MaxMana) gemBonus = (int)((bonusValue * 2) - 2); //mana
			else if (bonusType == (int)eProperty.MaxHealth) gemBonus = (int)(bonusValue / 4); //HP
			else if (bonusType <= (int)eProperty.Resist_Last) gemBonus = (int)((bonusValue * 2) - 2);//resist
			else if (bonusType <= (int)eProperty.Skill_Last) gemBonus = (int)((bonusValue - 1) * 5);//skill
			else gemBonus = 1;// focus
			if (gemBonus < 1) gemBonus = 1;

			return gemBonus;
		}

		/// <summary>
		/// Get the % overcharge penality
		/// </summary>
		/// <param name="maxBonusLevel"></param>
		/// <param name="bonusLevel"></param>
		/// <returns></returns>
		protected int GetOverchargePenality(int maxBonusLevel, int bonusLevel)
		{
			int diff = bonusLevel - maxBonusLevel;
			if (diff == 1) return -10;
			else if (diff == 2) return -20;
			else if (diff == 3) return -30;
			else if (diff == 4) return -50;
			else return -70;
		}

		/// <summary>
        /// Get the chance to preserve item while overcharging
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <param name="maxBonusLevel"></param>
		/// <param name="bonusLevel"></param>
		/// <returns></returns>
		protected int CalculateChanceToPreserveItem(GamePlayer player, DbInventoryItem item, int maxBonusLevel, int bonusLevel)
		{
			return CalculateChanceToPreserveItem(player, item, maxBonusLevel, bonusLevel, CombineMaterials.Of(player.TradeWindow));
		}

		/// <summary>Get the chance to preserve item while overcharging, for these materials</summary>
		protected int CalculateChanceToPreserveItem(GamePlayer player, DbInventoryItem item, int maxBonusLevel, int bonusLevel, CombineMaterials materials)
		{
			var gemQualities = new System.Collections.Generic.List<int>();
			lock (materials.Lock)
			{
				foreach (DbInventoryItem gem in materials.Items)
					gemQualities.Add(gem.Quality);
			}
			return PreserveChance(item.Quality, gemQualities, player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting));
		}

		/// <summary>CalculateChanceToPreserveItem for an item quality, the gems' qualities and spellcrafting skill (percent).</summary>
		public static int PreserveChance(int itemQuality, System.Collections.Generic.IEnumerable<int> gemQualities, int spellcraftSkill)
		{
			int baseChance = 0;

			double gemModifier = 0;
			foreach (int gemQuality in gemQualities)
				gemModifier += (gemQuality - 92) * 1.8;

			gemModifier = gemModifier * itemQuality / 100;

			int intSkillMod = PreserveSkillModifier(spellcraftSkill);

			int finalChances = (int)(intSkillMod + gemModifier + baseChance);
			if (finalChances > 100) finalChances = 100;
			else if (finalChances < 0) finalChances = 0;

			return finalChances;
		}

		/// <summary>The spellcrafting-skill part of the preserve chance (-50 below 50 skill .. +50 at 1000).</summary>
		public static int PreserveSkillModifier(int intSkill)
		{
			int intSkillMod;
			if (intSkill < 50) { intSkillMod = -50; }
			else if (intSkill < 100) { intSkillMod = -45; }
			else if (intSkill < 150) { intSkillMod = -40; }
			else if (intSkill < 200) { intSkillMod = -35; }
			else if (intSkill < 250) { intSkillMod = -30; }
			else if (intSkill < 300) { intSkillMod = -25; }
			else if (intSkill < 350) { intSkillMod = -20; }
			else if (intSkill < 400) { intSkillMod = -15; }
			else if (intSkill < 450) { intSkillMod = -10; }
			else if (intSkill < 500) { intSkillMod = -5; }
			else if (intSkill < 550) { intSkillMod = 0; }
			else if (intSkill < 600) { intSkillMod = 5; }
			else if (intSkill < 650) { intSkillMod = 10; }
			else if (intSkill < 700) { intSkillMod = 15; }
			else if (intSkill < 750) { intSkillMod = 20; }
			else if (intSkill < 800) { intSkillMod = 25; }
			else if (intSkill < 850) { intSkillMod = 30; }
			else if (intSkill < 900) { intSkillMod = 35; }
			else if (intSkill < 950) { intSkillMod = 40; }
			else if (intSkill < 1000) { intSkillMod = 45; }
			else { intSkillMod = 50; }

			return intSkillMod;
		}

		#endregion

	}
}
