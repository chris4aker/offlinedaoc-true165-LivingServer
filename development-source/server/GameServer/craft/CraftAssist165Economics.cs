using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>One planned craft for the crafting assistant: the recipe and its expected economics (average luck).</summary>
    public sealed class CraftPlanStep
    {
        public Recipe Recipe;
        public AbstractCraftingSkill Skill;
        public long MaterialCost;       // one craft from an empty backpack: whole packs, as merchants sell them
        public double MaterialCostPerCraft; // the run average: a pack's leftovers serve the next crafts
        public long SellValue;          // 0 when not selling (or the product cannot be sold)
        public int CraftSeconds;        // normal player time, failures take it too
        public double PMake;
        public double PGain;
        public double CostPerPoint;     // expected net copper per point of the target (can be negative: a profit)
        public double SecondsPerPoint;
        public ushort RecipeId;         // the CraftedItem id (what the client sends to make it)
        public int PointsPerGain;       // target points one gain gives (secondaries: one per matching ingredient entry)
    }

    /// <summary>The expected path from a skill now to a goal (average luck).</summary>
    public sealed class CraftEstimate
    {
        public bool Feasible;
        public byte Reason;             // op 40 reason codes: 0 none, 1 already at the goal, 5 nothing can raise it
        public int From;
        public int Goal;
        public int Reached;             // where the path ends (= Goal when feasible)
        public double Crafts;
        public double Seconds;
        public double MaterialCost;
        public double Resale;
        public double NetCost;
        public Recipe FirstRecipe;
        public ushort FirstRecipeId;
        public List<(eCraftingSkill Skill, int Now, int Projected, double EtaSeconds)> Skills = new();
    }

    /// <summary>
    /// Crafting assistant economics (a QoL option, not in 1.65): material prices, merchant sell prices, normal player
    /// craft times and the cheapest-recipe planner. It reads the real crafting rules and changes none of them; the
    /// chance / time / gain helpers below mirror AbstractCraftingSkill and its overrides line for line.
    /// </summary>
    public static class CraftAssist165Economics
    {
        public const byte OpStatus = 40, OpEstimate = 41, OpRecipes = 42;

        /// <summary>trains bits of op 42 (and of <see cref="TrainsBit"/>)</summary>
        public const byte TrainsCloth = 1, TrainsLeather = 2, TrainsMetal = 4, TrainsWood = 8, TrainsHerb = 16, TrainsGem = 32;

        private const int SkillSlots = 16;      // eCraftingSkill values 0..15
        private const int MaxSteps = 20000;     // safety stop for the simulation

        #region Pure 1.65 rules (mirrors of AbstractCraftingSkill and its overrides)

        /// <summary>AbstractCraftingSkill.GetItemCon: -3 grey .. 3 purple (impossible).</summary>
        public static int ItemCon(int crafterSkill, int itemCraftingLevel)
        {
            int diff = itemCraftingLevel - crafterSkill;
            if (diff <= -50)
                return -3;
            if (diff <= -31)
                return -2;
            if (diff <= -11)
                return -1;
            if (diff <= 0)
                return 0;
            if (diff <= 19)
                return 1;
            if (diff <= 49)
                return 2;
            return 3;
        }

        /// <summary>AbstractCraftingSkill.CalculateChanceToMakeItem for a con (percent).</summary>
        public static int MakeChance(int con)
        {
            switch (Math.Clamp(con, -3, 3))
            {
                case -3:
                case -2:
                case -1:
                    return 100;
                case 0:
                    return 100 - 8;
                case 1:
                    return 100 - 16;
                case 2:
                    return 100 - 32;
                default:
                    return 0;
            }
        }

        /// <summary>AbstractCraftingSkill.CalculateChanceToGainPoint for a con (percent), capital-city bonus included.</summary>
        public static int GainChance(int con, bool inCapitalCity, int craftingSkillBonus)
        {
            int chance;
            switch (Math.Clamp(con, -3, 3))
            {
                case -2: chance = 15; break;
                case -1: chance = 30; break;
                case 0: chance = 45; break;
                case 1: chance = 55; break;
                case 2: chance = 65; break;
                default: return 0;      // grey and purple
            }

            if (inCapitalCity)
            {
                chance += craftingSkillBonus;
                if (chance < 0)
                    chance = 0;
                if (chance > 100)
                    chance = 100;
            }
            return chance;
        }

        /// <summary>The materials count of GetCraftingTime (a count over 100 counts a tenth).</summary>
        public static int MaterialsCount(IList<Ingredient> ingredients)
        {
            ushort materialsCount = 0;
            for (int i = 0; i < ingredients.Count; i++)
            {
                int countMod = ingredients[i].Count;
                if (countMod > 100)
                    countMod /= 10;
                materialsCount += (ushort)countMod;
            }
            return materialsCount;
        }

        /// <summary>GetCraftingTime's first part: recipe level and materials, before speed bonuses.</summary>
        public static int BaseCraftTime(int recipeLevel, int materialsCount)
        {
            double baseMultiplier = (recipeLevel / 100) + 1;   // integer division, as in GetCraftingTime
            if (baseMultiplier < 1)
                baseMultiplier = 1;
            return (int)(baseMultiplier * materialsCount / 4);
        }

        /// <summary>GetCraftingTime's last part: the con factor (grey 0.4, green 0.6, blue 0.8) and the 1..MAX_CRAFT_TIME clamp.</summary>
        public static int FinishCraftTime(int craftingTime, int con, int maxCraftTime)
        {
            double mod = con switch { -3 => 0.4, -2 => 0.6, -1 => 0.8, _ => 1.0 };
            craftingTime = (int)(craftingTime * mod);
            if (craftingTime < 1)
                craftingTime = 1;
            else if (maxCraftTime > 0 && craftingTime > maxCraftTime)
                craftingTime = maxCraftTime;
            return craftingTime;
        }

        /// <summary>
        /// The skill a material trains in AbstractCraftingSkill.GainCraftingSkillPoints (and the secondary
        /// CheckSecondCraftingSkillRequirement checks for it): cloth square / heavy thread, leather square, metal bars,
        /// wooden boards. NoCrafting for any other model.
        /// </summary>
        public static eCraftingSkill SecondaryOfMaterialModel(int model) => model switch
        {
            522 or 537 => eCraftingSkill.ClothWorking,
            521 => eCraftingSkill.LeatherCrafting,
            519 => eCraftingSkill.MetalWorking,
            520 => eCraftingSkill.WoodWorking,
            _ => eCraftingSkill.NoCrafting,
        };

        /// <summary>Trades whose GainCraftingSkillPoints adds the per-ingredient secondary points (they call the base).</summary>
        public static bool GainsPerIngredient(eCraftingSkill trade) =>
            trade is eCraftingSkill.Tailoring or eCraftingSkill.ArmorCrafting or eCraftingSkill.WeaponCrafting or eCraftingSkill.Fletching;

        /// <summary>The extra secondary point a trade's gain always adds: Alchemy -> Herbcraft, Spellcrafting -> Gemcutting.</summary>
        public static eCraftingSkill FixedSecondaryGain(eCraftingSkill trade) => trade switch
        {
            eCraftingSkill.Alchemy => eCraftingSkill.HerbalCrafting,
            eCraftingSkill.SpellCrafting => eCraftingSkill.GemCutting,
            _ => eCraftingSkill.NoCrafting,
        };

        /// <summary>Secondary skills stop at subSkillCap (1300); the main trades have no cap.</summary>
        public static bool IsCappedSecondary(eCraftingSkill skill) =>
            skill is eCraftingSkill.ClothWorking or eCraftingSkill.LeatherCrafting or eCraftingSkill.MetalWorking or
                eCraftingSkill.WoodWorking or eCraftingSkill.HerbalCrafting or eCraftingSkill.GemCutting;

        /// <summary>op 42 trains bit of a secondary skill (0 for any other skill).</summary>
        public static byte TrainsBit(eCraftingSkill skill) => skill switch
        {
            eCraftingSkill.ClothWorking => TrainsCloth,
            eCraftingSkill.LeatherCrafting => TrainsLeather,
            eCraftingSkill.MetalWorking => TrainsMetal,
            eCraftingSkill.WoodWorking => TrainsWood,
            eCraftingSkill.HerbalCrafting => TrainsHerb,
            eCraftingSkill.GemCutting => TrainsGem,
            _ => 0,
        };

        /// <summary>GameMerchant.OnPlayerAppraise for a stack of count items (the merchant's sell ratio applied).</summary>
        public static long AppraiseValue(long price, int count, int packSize, int sellRatio, bool canSell)
        {
            if (!canSell)
                return 0;
            int itemCount = Math.Max(1, count);
            int pack = Math.Max(1, packSize);
            long val = price * itemCount / pack * sellRatio / 100;
            if (price == 1 && val == 0)
                val = price * itemCount / pack;
            return val;
        }

        #endregion

        #region Recipe data (loaded once per trade and realm)

        /// <summary>A recipe with everything the planner needs that does not depend on the player.</summary>
        public sealed class RecipeInfo
        {
            public ushort Id;
            public Recipe Recipe;
            public AbstractCraftingSkill Skill;
            public eCraftingSkill Trade;
            public int Level;
            public long MaterialCost;           // one craft from an empty backpack (whole packs)
            public double MaterialCostPerCraft; // count x price / pack size: what a run pays per craft on average
            public bool Buyable;                // every ingredient has a merchant price
            public bool Plannable;              // buyable and the product goes in the backpack (not a siege engine)
            public int MaterialsCount;
            public int SecondaryMinimum;        // GetSecondaryCraftingSkillMinimumLevel (<= 0: none)
            public eCraftingSkill[] SecondaryChecks = Array.Empty<eCraftingSkill>();  // skills that minimum applies to
            public eCraftingSkill[] SecondaryGains = Array.Empty<eCraftingSkill>();   // one entry per point a gain adds
            public byte Trains;                 // op 42 trains bits
        }

        private static readonly ConcurrentDictionary<(eCraftingSkill Trade, int Realm), RecipeInfo[]> TradeRecipes = new();
        private static readonly ConcurrentDictionary<Recipe, ushort> RecipeIds = new();

        /// <summary>Forget the loaded recipe lists (after a database reload).</summary>
        public static void ClearCache()
        {
            TradeRecipes.Clear();
            RecipeIds.Clear();
        }

        /// <summary>The CraftedItem id of a recipe the planner has loaded (0 if unknown).</summary>
        public static ushort RecipeIdOf(Recipe recipe) => recipe != null && RecipeIds.TryGetValue(recipe, out ushort id) ? id : (ushort)0;

        /// <summary>Builds the planner data of one recipe from the real crafting classes (no player needed).</summary>
        public static RecipeInfo Describe(ushort id, Recipe recipe, AbstractCraftingSkill skill)
        {
            eCraftingSkill trade = recipe.RequiredCraftingSkill;
            List<Ingredient> ingredients = recipe.Ingredients;
            var info = new RecipeInfo
            {
                Id = id,
                Recipe = recipe,
                Skill = skill,
                Trade = trade,
                Level = recipe.Level,
                MaterialCost = MaterialCost(recipe),
                MaterialCostPerCraft = MaterialCostPerCraft(recipe),
                MaterialsCount = MaterialsCount(ingredients),
                SecondaryMinimum = skill?.GetSecondaryCraftingSkillMinimumLevel(recipe) ?? 0,
                Buyable = true,
            };

            var checks = new List<eCraftingSkill>(4);
            var gains = new List<eCraftingSkill>(4);
            foreach (Ingredient ingredient in ingredients)
            {
                if (ingredient.Material == null || ingredient.Material.Price <= 0)
                    info.Buyable = false;
                eCraftingSkill secondary = SecondaryOfMaterialModel(ingredient.Material?.Model ?? 0);
                if (secondary == eCraftingSkill.NoCrafting)
                    continue;
                if (!checks.Contains(secondary))
                    checks.Add(secondary);
                if (GainsPerIngredient(trade))
                    gains.Add(secondary);       // base GainCraftingSkillPoints: one point per ingredient entry
            }
            eCraftingSkill fixedGain = FixedSecondaryGain(trade);
            if (fixedGain != eCraftingSkill.NoCrafting)
                gains.Add(fixedGain);

            // a siege engine is placed in the world, not in the backpack: the assistant cannot run it or sell it
            info.Plannable = info.Buyable && !IsSiegeEngine(recipe.Product);
            info.SecondaryChecks = info.SecondaryMinimum > 0 ? checks.ToArray() : Array.Empty<eCraftingSkill>();
            info.SecondaryGains = gains.ToArray();
            byte trains = TrainsBit(trade);
            foreach (eCraftingSkill g in info.SecondaryGains)
                trains |= TrainsBit(g);
            info.Trains = trains;
            return info;
        }

        /// <summary>The recipes of a trade for a realm (products of that realm or of none), real Recipe objects from RecipeDB.</summary>
        public static RecipeInfo[] RecipesOf(eCraftingSkill trade, eRealm realm)
        {
            return TradeRecipes.GetOrAdd((trade, (int)realm), key => LoadRecipes(key.Trade, (eRealm)key.Realm));
        }

        private static RecipeInfo[] LoadRecipes(eCraftingSkill trade, eRealm realm)
        {
            var list = new List<RecipeInfo>();
            AbstractCraftingSkill skill = CraftingMgr.getSkillbyEnum(trade);
            if (skill == null || GameServer.Database == null)
                return list.ToArray();

            foreach (DbCraftedItem crafted in DOLDB<DbCraftedItem>.SelectObjects(DB.Column("CraftingSkillType").IsEqualTo((int)trade)))
            {
                if (!ushort.TryParse(crafted.CraftedItemID, out ushort id))
                    continue;
                // the realm first, so other realms' recipes are never loaded
                DbItemTemplate product = GameServer.Database.FindObjectByKey<DbItemTemplate>(crafted.Id_nb);
                if (product == null || product.Realm != 0 && product.Realm != (int)realm)
                    continue;

                Recipe recipe;
                try
                {
                    recipe = RecipeDB.FindBy(id);
                }
                catch (Exception)
                {
                    continue;   // a recipe RecipeDB found invalid
                }
                if (recipe?.Product == null || recipe.RequiredCraftingSkill != trade)
                    continue;

                list.Add(Describe(id, recipe, skill));
                RecipeIds[recipe] = id;
            }

            list.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Id.CompareTo(b.Id));
            return list.ToArray();
        }

        /// <summary>The trades whose recipes can raise a skill: itself, plus the main trades whose gains add to that secondary.</summary>
        public static eCraftingSkill[] TradesRaising(eCraftingSkill target) => target switch
        {
            eCraftingSkill.ClothWorking or eCraftingSkill.LeatherCrafting or eCraftingSkill.MetalWorking or eCraftingSkill.WoodWorking =>
                [target, eCraftingSkill.Tailoring, eCraftingSkill.ArmorCrafting, eCraftingSkill.WeaponCrafting, eCraftingSkill.Fletching],
            eCraftingSkill.HerbalCrafting => [target, eCraftingSkill.Alchemy],
            eCraftingSkill.GemCutting => [target, eCraftingSkill.SpellCrafting],
            eCraftingSkill.NoCrafting => [],
            _ => [target],
        };

        #endregion

        #region Prices, times, skills

        /// <summary>
        /// What a merchant charges for a count of a material: whole packs (GameMerchant.OnPlayerBuy sells `number` packs of
        /// PackSize items for number x Price). Pack size 1 (nearly every material) is count x price.
        /// </summary>
        public static long PackCost(long price, int packSize, int count)
        {
            if (count <= 0)
                return 0;
            int pack = Math.Max(1, packSize);
            return (count + pack - 1) / pack * price;
        }

        /// <summary>All ingredients for one craft from an empty backpack, at merchant price in whole packs.</summary>
        public static long MaterialCost(Recipe recipe)
        {
            if (recipe == null)
                return 0;
            long total = 0;
            foreach (Ingredient ingredient in recipe.Ingredients)
            {
                if (ingredient.Material != null)
                    total += PackCost(ingredient.Material.Price, ingredient.Material.PackSize, ingredient.Count);
            }
            return total;
        }

        /// <summary>The average material cost of a craft over a run (a pack's leftovers serve the next crafts).</summary>
        public static double MaterialCostPerCraft(Recipe recipe)
        {
            if (recipe == null)
                return 0;
            double total = 0;
            foreach (Ingredient ingredient in recipe.Ingredients)
            {
                if (ingredient.Material != null)
                    total += (double)ingredient.Material.Price * ingredient.Count / Math.Max(1, ingredient.Material.PackSize);
            }
            return total;
        }

        /// <summary>What buying the missing materials for one craft costs (missing count x merchant price, per material name).</summary>
        public static long MissingMaterialCost(GamePlayer player, Recipe recipe)
        {
            if (player?.Inventory == null || recipe == null)
                return MaterialCost(recipe);
            Dictionary<string, int> have = BackpackCounts(player);
            var need = new Dictionary<string, (int Count, DbItemTemplate Material)>();
            foreach (Ingredient ingredient in recipe.Ingredients)
            {
                if (ingredient.Material == null)
                    continue;
                string name = ingredient.Material.Name;
                need[name] = need.TryGetValue(name, out var n) ? (n.Count + ingredient.Count, n.Material) : (ingredient.Count, ingredient.Material);
            }
            long total = 0;
            foreach (var kv in need)
            {
                int missing = kv.Value.Count - (have.TryGetValue(kv.Key, out int c) ? c : 0);
                total += PackCost(kv.Value.Material.Price, kv.Value.Material.PackSize, missing);
            }
            return total;
        }

        /// <summary>Backpack item counts by name (CheckRawMaterials matches materials by name).</summary>
        public static Dictionary<string, int> BackpackCounts(GamePlayer player)
        {
            var counts = new Dictionary<string, int>();
            if (player?.Inventory == null)
                return counts;
            lock (player.Inventory.Lock)
            {
                foreach (DbInventoryItem item in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                {
                    if (item?.Name == null)
                        continue;
                    counts[item.Name] = (counts.TryGetValue(item.Name, out int c) ? c : 0) + item.Count;
                }
            }
            return counts;
        }

        // a stand-in inventory item so the real merchant sale rule (FrontierSupplyMerchantPolicy.CanSell) reads the product
        private sealed class ProductProbe : DbInventoryItem
        {
            public ProductProbe(DbItemTemplate template) : base(template) { }
        }

        /// <summary>
        /// What a merchant pays for what one craft makes (OnPlayerAppraise of a pack-size stack, ITEM_SELL_RATIO applied).
        /// Siege engines are placed in the world, not in the backpack, so they sell for nothing.
        /// </summary>
        public static long SellValue(GamePlayer player, DbItemTemplate product)
        {
            if (product == null || IsSiegeEngine(product))
                return 0;
            var probe = new ProductProbe(product);
            bool canSell = FrontierSupplyMerchantPolicy.CanSell(probe);
            int count = Math.Max(1, product.PackSize);
            return AppraiseValue(product.Price, count, product.PackSize, ServerProperties.Properties.ITEM_SELL_RATIO, canSell);
        }

        public static bool IsSiegeEngine(DbItemTemplate product) => product != null && (eObjectType)product.Object_Type is
            eObjectType.SiegeBalista or eObjectType.SiegeCatapult or eObjectType.SiegeCauldron or eObjectType.SiegeRam or eObjectType.SiegeTrebuchet;

        /// <summary>The normal player craft time for this player's skill now (GMs too: no 0-second shortcut).</summary>
        public static int PlayerCraftSeconds(GamePlayer player, Recipe recipe)
        {
            if (recipe == null)
                return 1;
            return PlayerCraftSeconds(player, recipe, player?.GetCraftingSkillValue(recipe.RequiredCraftingSkill) ?? 0);
        }

        /// <summary>GetCraftingTime's player formula at a given trade skill (for simulated skills).</summary>
        public static int PlayerCraftSeconds(GamePlayer player, Recipe recipe, int tradeSkill)
        {
            if (recipe == null)
                return 1;
            int before = TimeBeforeCon(player, recipe.Level, MaterialsCount(recipe.Ingredients));
            return FinishCraftTime(before, ItemCon(tradeSkill, recipe.Level), ServerProperties.Properties.MAX_CRAFT_TIME);
        }

        // GetCraftingTime up to the con factor: base time, crafting speed and (outside 1.65) relic and keep bonuses
        private static int TimeBeforeCon(GamePlayer player, int recipeLevel, int materialsCount)
        {
            int craftingTime = BaseCraftTime(recipeLevel, materialsCount);
            if (player == null)
                return craftingTime;
            craftingTime = (int)(craftingTime / player.CraftingSpeed);
            if (!Classic165Mode.IsOn)
            {
                craftingTime = (int)(craftingTime * (1 - (.05 * RelicMgr.GetRelicCount(player.Realm))));
                if (Keeps.KeepBonusMgr.RealmHasBonus(Keeps.eKeepBonusType.Craft_Timers_5, player.Realm))
                    craftingTime = (int)(craftingTime / 1.05);
                else if (Keeps.KeepBonusMgr.RealmHasBonus(Keeps.eKeepBonusType.Craft_Timers_3, player.Realm))
                    craftingTime = (int)(craftingTime / 1.03);
            }
            return craftingTime;
        }

        /// <summary>A copy of the player's crafting skills.</summary>
        public static Dictionary<eCraftingSkill, int> SkillsOf(GamePlayer player)
        {
            if (player == null)
                return new Dictionary<eCraftingSkill, int>();
            lock (player._craftingLock)
            {
                return new Dictionary<eCraftingSkill, int>(player.CraftingSkills);
            }
        }

        #endregion

        #region Planner

        /// <summary>The player-dependent context of a planning call: candidates with their time before the con factor.</summary>
        public sealed class PlanContext
        {
            public eCraftingSkill Target;
            public bool Sell;
            public bool InCapitalCity;
            public int CraftingSkillBonus;
            public int MaxCraftTime;
            public readonly List<Candidate> Candidates = new();
        }

        public sealed class Candidate
        {
            public RecipeInfo Info;
            public long SellValue;              // 0 when not selling
            public int TimeBeforeCon;
        }

        /// <summary>Builds the candidates that can raise a target for this player (realm, crafting speed, sell values).</summary>
        public static PlanContext BuildContext(GamePlayer player, eCraftingSkill target, bool sell)
        {
            var ctx = new PlanContext
            {
                Target = target,
                Sell = sell,
                InCapitalCity = player?.CurrentRegion?.IsCapitalCity == true,
                CraftingSkillBonus = player?.CurrentRegion?.IsCapitalCity == true ? player.CraftingSkillBonus : 0,
                MaxCraftTime = ServerProperties.Properties.MAX_CRAFT_TIME,
            };
            if (player == null)
                return ctx;

            foreach (eCraftingSkill trade in TradesRaising(target))
            {
                foreach (RecipeInfo info in RecipesOf(trade, player.Realm))
                {
                    if (!info.Plannable || GameServer.ServerRules != null && !GameServer.ServerRules.IsAllowedToCraft(player, info.Recipe.Product))
                        continue;
                    if (trade != target && Array.IndexOf(info.SecondaryGains, target) < 0)
                        continue;   // a main-trade recipe without the target's material
                    ctx.Candidates.Add(new Candidate
                    {
                        Info = info,
                        SellValue = sell ? SellValue(player, info.Recipe.Product) : 0,
                        TimeBeforeCon = TimeBeforeCon(player, info.Level, info.MaterialsCount),
                    });
                }
            }
            return ctx;
        }

        /// <summary>How many target points one gain of this recipe gives at these skills (secondaries stop at 1300).</summary>
        public static int PointsPerGain(RecipeInfo info, eCraftingSkill target, int[] skills)
        {
            int points = 0;
            if (info.Trade == target)
                points = 1;
            else
            {
                foreach (eCraftingSkill g in info.SecondaryGains)
                    if (g == target)
                        points++;
            }
            if (points > 0 && IsCappedSecondary(target))
                points = Math.Min(points, Math.Max(0, AbstractCraftingSkill.subSkillCap - skills[(int)target]));
            return points;
        }

        /// <summary>CheckSecondCraftingSkillRequirement at these skills.</summary>
        public static bool SecondaryRequirementMet(RecipeInfo info, int[] skills)
        {
            if (info.SecondaryMinimum <= 0)
                return true;
            foreach (eCraftingSkill s in info.SecondaryChecks)
                if (skills[(int)s] < info.SecondaryMinimum)
                    return false;
            return true;
        }

        /// <summary>Evaluates one candidate at these skills; false when it cannot raise the target (grey, purple, requirement).</summary>
        public static bool Evaluate(PlanContext ctx, Candidate c, int[] skills, out double pMake, out double pGain, out int seconds, out int points)
        {
            RecipeInfo info = c.Info;
            int con = ItemCon(skills[(int)info.Trade], info.Level);
            pMake = MakeChance(con) / 100.0;
            pGain = GainChance(con, ctx.InCapitalCity, ctx.CraftingSkillBonus) / 100.0;
            seconds = FinishCraftTime(c.TimeBeforeCon, con, ctx.MaxCraftTime);
            points = 0;
            if (!info.Plannable || pMake <= 0 || pGain <= 0 || !SecondaryRequirementMet(info, skills))
                return false;
            points = PointsPerGain(info, ctx.Target, skills);
            return points > 0;
        }

        /// <summary>The candidate with the lowest expected net cost per target point (ties: fewer seconds per point, then lower id).</summary>
        public static CraftPlanStep Pick(PlanContext ctx, int[] skills)
        {
            Candidate best = null;
            double bestCost = 0, bestSecs = 0, bestMake = 0, bestGain = 0;
            int bestSeconds = 0, bestPoints = 0;
            List<Candidate> list = ctx.Candidates;
            for (int i = 0; i < list.Count; i++)
            {
                Candidate c = list[i];
                if (!Evaluate(ctx, c, skills, out double pMake, out double pGain, out int seconds, out int points))
                    continue;
                // per attempt: materials pMake*cost, resale pMake*sell, pMake*pGain gains of `points` points
                double cost = (c.Info.MaterialCostPerCraft - c.SellValue) / (pGain * points);
                double secs = seconds / (pMake * pGain * points);
                if (best != null)
                {
                    double eps = 1e-9 * Math.Max(1.0, Math.Abs(bestCost));
                    if (cost > bestCost + eps)
                        continue;
                    if (Math.Abs(cost - bestCost) <= eps && (secs > bestSecs + 1e-9 || Math.Abs(secs - bestSecs) <= 1e-9 && c.Info.Id >= best.Info.Id))
                        continue;
                }
                best = c;
                bestCost = cost;
                bestSecs = secs;
                bestMake = pMake;
                bestGain = pGain;
                bestSeconds = seconds;
                bestPoints = points;
            }
            if (best == null)
                return null;
            return new CraftPlanStep
            {
                Recipe = best.Info.Recipe,
                Skill = best.Info.Skill,
                RecipeId = best.Info.Id,
                MaterialCost = best.Info.MaterialCost,
                MaterialCostPerCraft = best.Info.MaterialCostPerCraft,
                SellValue = best.SellValue,
                CraftSeconds = bestSeconds,
                PMake = bestMake,
                PGain = bestGain,
                CostPerPoint = bestCost,
                SecondsPerPoint = bestSecs,
                PointsPerGain = bestPoints,
            };
        }

        /// <summary>Skills as a flat array (missing skills read -1, as GetCraftingSkillValue does).</summary>
        public static int[] SkillArray(IReadOnlyDictionary<eCraftingSkill, int> skills)
        {
            var arr = new int[SkillSlots];
            for (int i = 0; i < SkillSlots; i++)
                arr[i] = -1;
            if (skills != null)
            {
                foreach (var kv in skills)
                {
                    if ((int)kv.Key >= 0 && (int)kv.Key < SkillSlots)
                        arr[(int)kv.Key] = kv.Value;
                }
            }
            return arr;
        }

        /// <summary>The best recipe to raise a target at these (possibly simulated) skills; null when nothing can raise it.</summary>
        public static CraftPlanStep BestRecipe(GamePlayer player, eCraftingSkill target, IReadOnlyDictionary<eCraftingSkill, int> skills, bool sell)
        {
            PlanContext ctx = BuildContext(player, target, sell);
            return Pick(ctx, SkillArray(skills ?? SkillsOf(player)));
        }

        /// <summary>
        /// One expected gain of a step: the trade +1 (secondaries stop at 1300) and each secondary entry +1 below 1300,
        /// in GainCraftingSkillPoints order.
        /// </summary>
        public static void ApplyGain(RecipeInfo info, int[] skills)
        {
            if (!IsCappedSecondary(info.Trade) || skills[(int)info.Trade] < AbstractCraftingSkill.subSkillCap)
                skills[(int)info.Trade]++;
            foreach (eCraftingSkill g in info.SecondaryGains)
            {
                if (skills[(int)g] < AbstractCraftingSkill.subSkillCap)
                    skills[(int)g]++;
            }
        }

        /// <summary>The expected (average luck) path from the player's skills now to the goal.</summary>
        public static CraftEstimate Estimate(GamePlayer player, eCraftingSkill target, int goal, bool sell)
        {
            return Simulate(BuildContext(player, target, sell), SkillsOf(player), goal);
        }

        /// <summary>
        /// Walks the expected path gain by gain: re-picks the cheapest recipe at each step, raises every skill the
        /// gain raises (so secondary minimums unlock as they would) and sums crafts, time, cost and resale.
        /// </summary>
        public static CraftEstimate Simulate(PlanContext ctx, IReadOnlyDictionary<eCraftingSkill, int> start, int goal)
        {
            int[] skills = SkillArray(start);
            int[] before = new int[SkillSlots];
            var eta = new double[SkillSlots];
            int t = (int)ctx.Target;
            var est = new CraftEstimate { From = skills[t], Goal = goal, Feasible = true };

            // recipes grey at the trade skill now stay grey (skills only rise): drop them up front and as we go
            PruneGrey(ctx, skills);
            int steps = 0;
            while (skills[t] < goal)
            {
                if (++steps > MaxSteps)
                {
                    est.Feasible = false;
                    est.Reason = 5;
                    break;
                }
                CraftPlanStep step = Pick(ctx, skills);
                if (step == null)
                {
                    est.Feasible = false;
                    est.Reason = 5;
                    break;
                }
                if (est.FirstRecipe == null)
                {
                    est.FirstRecipe = step.Recipe;
                    est.FirstRecipeId = step.RecipeId;
                }

                // expected attempts for one gain = 1 / (pMake * pGain); only successes use materials and sell
                double attempts = 1.0 / (step.PMake * step.PGain);
                est.Crafts += attempts;
                est.Seconds += attempts * step.CraftSeconds;
                est.MaterialCost += step.MaterialCostPerCraft / step.PGain;
                est.Resale += step.SellValue / step.PGain;

                Array.Copy(skills, before, SkillSlots);
                ApplyGain(FindInfo(ctx, step.RecipeId), skills);
                for (int i = 0; i < SkillSlots; i++)
                {
                    if (skills[i] != before[i])
                        eta[i] = est.Seconds;   // the time this skill reaches its latest value
                }
                PruneGrey(ctx, skills);
            }

            est.Reached = skills[t];
            est.NetCost = est.MaterialCost - est.Resale;
            if (est.From >= goal)
                est.Reason = 1;

            int[] first = SkillArray(start);
            est.Skills.Add((ctx.Target, first[t], skills[t], est.Seconds));
            for (int i = 0; i < SkillSlots && est.Skills.Count < 8; i++)
            {
                if (i == t || skills[i] == first[i])
                    continue;
                est.Skills.Add(((eCraftingSkill)i, first[i], skills[i], eta[i]));
            }
            return est;
        }

        private static RecipeInfo FindInfo(PlanContext ctx, ushort id)
        {
            List<Candidate> list = ctx.Candidates;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Info.Id == id)
                    return list[i].Info;
            }
            return null;
        }

        // drops the candidates grey at their trade skill (no LINQ / lambda: this runs every step)
        private static void PruneGrey(PlanContext ctx, int[] skills)
        {
            List<Candidate> list = ctx.Candidates;
            int keep = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Candidate c = list[i];
                if (ItemCon(skills[(int)c.Info.Trade], c.Info.Level) > -3)
                    list[keep++] = c;
            }
            if (keep < list.Count)
                list.RemoveRange(keep, list.Count - keep);
        }

        #endregion

        #region op 42

        /// <summary>
        /// op 42: every recipe of a trade for the player's realm with its economics, 6 rows a packet, then an end packet
        /// (0 rows). Row: id u16, trade level u16, material cost u32, sell value u32, secondary minimum u16, trains u8,
        /// status u8 (bit0 secondary requirement met, bit1 all materials in the backpack), craft seconds u16.
        /// </summary>
        public static void SendRecipes(GamePlayer player, eCraftingSkill trade)
        {
            if (player == null)
                return;
            foreach (byte[] payload in RecipePayloads(player, trade))
                Hire165.Send(player, OpRecipes, payload);
        }

        /// <summary>The op 42 payloads SendRecipes sends (payload[0] is body[4]).</summary>
        public static List<byte[]> RecipePayloads(GamePlayer player, eCraftingSkill trade)
        {
            const int rowsPerPacket = 6, rowSize = 18;
            var packets = new List<byte[]>();
            int[] skills = SkillArray(SkillsOf(player));
            Dictionary<string, int> backpack = BackpackCounts(player);
            RecipeInfo[] all = trade == eCraftingSkill.NoCrafting ? Array.Empty<RecipeInfo>() : RecipesOf(trade, player.Realm);

            var rows = new List<RecipeInfo>(all.Length);
            foreach (RecipeInfo info in all)
            {
                if (GameServer.ServerRules == null || GameServer.ServerRules.IsAllowedToCraft(player, info.Recipe.Product))
                    rows.Add(info);
            }

            for (int start = 0; start < rows.Count; start += rowsPerPacket)
            {
                int k = Math.Min(rowsPerPacket, rows.Count - start);
                var payload = new byte[4 + k * rowSize];
                payload[0] = (byte)trade;
                payload[1] = (byte)k;
                for (int r = 0; r < k; r++)
                {
                    RecipeInfo info = rows[start + r];
                    int o = 4 + r * rowSize;
                    long sell = SellValue(player, info.Recipe.Product);
                    int secs = FinishCraftTime(TimeBeforeCon(player, info.Level, info.MaterialsCount),
                        ItemCon(skills[(int)info.Trade], info.Level), ServerProperties.Properties.MAX_CRAFT_TIME);
                    byte status = 0;
                    if (SecondaryRequirementMet(info, skills))
                        status |= 1;
                    if (HasMaterials(info.Recipe, backpack))
                        status |= 2;
                    WriteRecipeRow(payload, o, info, sell, status, secs);
                }
                packets.Add(payload);
            }
            packets.Add([(byte)trade, 0, 0, 0]);   // end of the list
            return packets;
        }

        /// <summary>One 18-byte op 42 row at offset o.</summary>
        public static void WriteRecipeRow(byte[] payload, int o, RecipeInfo info, long sell, byte status, int seconds)
        {
            PutU16(payload, o, info.Id);
            PutU16(payload, o + 2, ClampU16(info.Level));
            PutU32(payload, o + 4, ClampU32(info.MaterialCost));
            PutU32(payload, o + 8, ClampU32(sell));
            PutU16(payload, o + 12, ClampU16(info.SecondaryMinimum));
            payload[o + 14] = info.Trains;
            payload[o + 15] = status;
            PutU16(payload, o + 16, ClampU16(seconds));
        }

        /// <summary>Whether the backpack holds every ingredient (by name, as CheckRawMaterials counts them).</summary>
        public static bool HasMaterials(Recipe recipe, Dictionary<string, int> backpack)
        {
            var need = new Dictionary<string, int>();
            foreach (Ingredient ingredient in recipe.Ingredients)
            {
                if (ingredient.Material == null)
                    return false;
                need[ingredient.Material.Name] = (need.TryGetValue(ingredient.Material.Name, out int n) ? n : 0) + ingredient.Count;
            }
            foreach (var kv in need)
            {
                if (!backpack.TryGetValue(kv.Key, out int have) || have < kv.Value)
                    return false;
            }
            return true;
        }

        public static ushort ClampU16(long v) => (ushort)Math.Clamp(v, 0, ushort.MaxValue);
        public static uint ClampU32(long v) => (uint)Math.Clamp(v, 0, uint.MaxValue);

        public static void PutU16(byte[] b, int o, int v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
        }

        public static void PutU32(byte[] b, int o, long v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }

        #endregion
    }
}
