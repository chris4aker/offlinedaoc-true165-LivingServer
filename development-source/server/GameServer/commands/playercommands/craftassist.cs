using System;

namespace DOL.GS.Commands
{
    /// <summary>The crafting assistant's commands (True 1.65 quality of life option, owner 2026-11-08). The new client sends
    /// these from its crafting window; answers come back as 'H' ops 40-42 (crafting_overhaul_spec).</summary>
    [CmdAttribute(
        "&craftassist",
        ePrivLevel.Player,
        "Crafting assistant: crafts toward a skill goal, buying and selling at merchant prices",
        "/craftassist recipes <trade id> - recipe costs and sell values",
        "/craftassist estimate <skill id> <goal> <sell 0|1> - cost and time to the goal",
        "/craftassist start <skill id> <goal> <buy 0|1> <sell 0|1> - start crafting toward the goal",
        "/craftassist stop - stop (no materials lost)",
        "/craftassist status - the run's numbers",
        "/craftassist enchantinfo <backpack slot> - what the item can be enchanted with",
        "/craftassist enchant <backpack slot> spell <gem id>[,<gem id>...] <overcharge 0|1> - craft and imbue gems",
        "/craftassist enchant <backpack slot> alch <tincture id> - craft and apply a tincture",
        "/craftassist enchantstop - stop the enchant helper")]
    public class CraftAssistCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null)
                return;
            if (args.Length < 2)
            {
                DisplaySyntax(client);
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "recipes":
                    if (!TryParse(args, 2, out eCraftingSkill trade))
                    {
                        DisplayMessage(client, "Use: /craftassist recipes <trade id>");
                        return;
                    }
                    CraftAssist165Economics.SendRecipes(player, trade);
                    return;

                case "estimate":
                    if (!TryParse(args, 2, out eCraftingSkill skill) || !TryGoal(args, 3, out int goal) || !TryFlag(args, 4, out bool sell))
                    {
                        DisplayMessage(client, "Use: /craftassist estimate <skill id> <goal> <sell 0|1>");
                        return;
                    }
                    CraftAssist165.SendEstimate(player, skill, goal, sell);
                    return;

                case "start":
                    if (!TryParse(args, 2, out eCraftingSkill target) || !TryGoal(args, 3, out int to) ||
                        !TryFlag(args, 4, out bool buy) || !TryFlag(args, 5, out bool sellToo))
                    {
                        DisplayMessage(client, "Use: /craftassist start <skill id> <goal> <buy 0|1> <sell 0|1>");
                        return;
                    }
                    CraftAssist165.Start(player, target, to, buy, sellToo);
                    return;

                case "stop":
                    CraftAssist165.Stop(player, CraftAssistReason.StoppedByPlayer, null);
                    return;

                case "status":
                    CraftAssist165.Status(player);
                    return;

                // enchant helper (CraftEnchant165): ops 43 / 44 of enchant_helper_spec
                case "enchantinfo":
                    if (args.Length < 3 || !CraftEnchant165.TryBackpackSlot(args[2], out int infoSlot))
                    {
                        DisplayMessage(client, "Use: /craftassist enchantinfo <backpack slot>");
                        return;
                    }
                    CraftEnchant165.SendInfo(player, infoSlot);
                    return;

                case "enchant":
                    OnEnchant(client, player, args);
                    return;

                case "enchantstop":
                    CraftEnchant165.Stop(player, EnchantReason.StoppedByPlayer, null);
                    return;

                case "enchantstatus":
                    CraftEnchant165.Status(player);
                    return;

                default:
                    DisplaySyntax(client);
                    return;
            }
        }

        // /craftassist enchant <slot> spell <id>[,<id>...] <overcharge 0|1>  or  /craftassist enchant <slot> alch <id>
        private void OnEnchant(GameClient client, GamePlayer player, string[] args)
        {
            const string use = "Use: /craftassist enchant <backpack slot> spell <gem recipe id>[,<id>...] <overcharge 0|1>  or  /craftassist enchant <backpack slot> alch <tincture recipe id>";
            if (args.Length < 5 || !CraftEnchant165.TryBackpackSlot(args[2], out int slot))
            {
                DisplayMessage(client, use);
                return;
            }
            switch (args[3].ToLowerInvariant())
            {
                case "spell":
                    if (!CraftEnchant165.TryRecipeIds(args[4], out var gems) || !TryFlag(args, 5, out bool overcharge))
                    {
                        DisplayMessage(client, use);
                        return;
                    }
                    CraftEnchant165.StartSpell(player, slot, gems, overcharge);
                    return;
                case "alch":
                    if (!CraftEnchant165.TryRecipeIds(args[4], out var tincture) || tincture.Count != 1)
                    {
                        DisplayMessage(client, use);
                        return;
                    }
                    CraftEnchant165.StartAlchemy(player, slot, tincture[0]);
                    return;
                default:
                    DisplayMessage(client, use);
                    return;
            }
        }

        /// <summary>A crafting skill id the player can train (any eCraftingSkill but NoCrafting / BasicCrafting).</summary>
        public static bool TryParse(string[] args, int at, out eCraftingSkill skill)
        {
            skill = eCraftingSkill.NoCrafting;
            if (args.Length <= at || !int.TryParse(args[at], out int id) || !Enum.IsDefined(typeof(eCraftingSkill), id))
                return false;
            skill = (eCraftingSkill)id;
            return skill != eCraftingSkill.NoCrafting && skill != eCraftingSkill.BasicCrafting;
        }

        public static bool TryGoal(string[] args, int at, out int goal)
        {
            goal = 0;
            return args.Length > at && int.TryParse(args[at], out goal) && goal > 0 && goal <= 1300;
        }

        public static bool TryFlag(string[] args, int at, out bool flag)
        {
            flag = false;
            if (args.Length <= at)
                return false;
            if (args[at] == "1") { flag = true; return true; }
            return args[at] == "0";
        }
    }
}
