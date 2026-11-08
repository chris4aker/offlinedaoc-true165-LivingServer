using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode: crafter gamebots have the gear they craft enchanted (owner 2026-11-08: "make sure bots can enchant gear
    /// they craft and weapons too like this enchant helper does (they have to have the skill level and pay the cost too)").
    /// - A weapon / armour / tailoring / fletching crafter bot that just made a piece (level 15+, 94%+ quality) may have it
    ///   spellcrafted by the best Spellcrafting bot of its realm and given a tincture by the best Alchemy bot of its realm.
    /// - Gems and tinctures come from the real recipes those bots can make (con not impossible), with the real gem bonuses,
    ///   the real imbue formulas (item max from level + quality, the spellcrafter cap skill / 20, no overcharge) and the
    ///   real tincture effects (proc, or charges by quality) - the item ends up as a player-enchanted one.
    /// - Nothing is free: the crafter pays every gem's / the tincture's recipe materials at merchant price (whole packs) plus
    ///   a 10% fee to each enchanter, and the enchanters gain skill as if they crafted the gems / tincture.
    /// Light at 1,000-2,000 bots a realm: recipe lists load once per realm, the best enchanters are found every few minutes
    /// from the live bot list (no database query), and only a share of crafted items is ever looked at.
    /// </summary>
    public static class BotEnchant165
    {
        public const int MinItemLevel = 15;
        public const int MinQuality = 94;
        public const int FeePercent = 10;
        public const int MaxGemSlots = 4;
        private const long RefreshMilliseconds = 5 * 60_000;

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BotEnchant165));

        public enum TinctureKind : byte { WeaponProc = 1, ArmourProc = 2, Charges = 3 }

        /// <summary>One spellcraft gem recipe: its product's bonus, recipe level and material cost.</summary>
        public sealed record GemOption(ushort RecipeId, string Name, int BonusType, int BonusValue, int Level, long Cost);

        /// <summary>One alchemy tincture recipe with the fields Alchemy.ApplyMagicalEffect copies onto the item.</summary>
        public sealed record TinctureOption(ushort RecipeId, string Name, int TypeDamage, int ProcSpellId, byte ProcChance,
            int SpellId, int LevelRequirement, int Level, long Cost)
        {
            public TinctureKind Kind => ProcSpellId != 0 ? (TypeDamage == 1 ? TinctureKind.WeaponProc : TinctureKind.ArmourProc) : TinctureKind.Charges;
        }

        /// <summary>What an enchant did (Cost = materials + fees the crafter paid; 0 when nothing was done).</summary>
        public sealed class Result
        {
            public List<GemOption> Gems = new();
            public TinctureOption Tincture;
            public long Materials;
            public long SpellFee;
            public long AlchemyFee;
            public long Cost => Materials + SpellFee + AlchemyFee;
            public bool Done => Gems.Count > 0 || Tincture != null;
        }

        #region Real formulas (SpellCrafting / Alchemy)

        /// <summary>SpellCrafting.GetItemMaxImbuePoints: the item's imbue points from its level and quality.</summary>
        public static int ItemMaxImbue(DbInventoryItem item) => item == null ? 0 : SpellCrafting.ItemMaxImbuePoints(item.Level, item.Quality);

        /// <summary>SpellCrafting.GetGemImbuePoints: one bonus's imbue cost.</summary>
        public static int GemPoints(int bonusType, int bonusValue) => SpellCrafting.GemImbuePoints(bonusType, bonusValue);

        /// <summary>Alchemy.GetItemMaxCharges: 2 below 94%, quality - 92 up to 99%, 10 at 100%.</summary>
        public static int MaxCharges(DbInventoryItem item) => Alchemy.MaxChargesForQuality(item.Quality);

        /// <summary>The spellcrafter's cap (SpellCrafting.IsAllowedToCombine: total imbue may not pass skill / 20).</summary>
        public static int SpellcrafterCap(int skill) => Math.Max(0, skill) / 20;

        /// <summary>SpellCrafting.GetTotalImbuePoints for the item's bonuses plus these gems (every bonus's points plus the
        /// biggest once more, halved; the item's first three bonuses counted, as there).</summary>
        public static int TotalImbue(DbInventoryItem item, IEnumerable<(int Type, int Value)> gems) =>
            SpellCrafting.TotalImbuePoints(item, gems);

        /// <summary>A recipe the crafter can make: its con is not impossible (AbstractCraftingSkill.GetItemCon below 3).</summary>
        public static bool CanMake(int skill, int recipeLevel) => CraftAssist165Economics.ItemCon(skill, recipeLevel) < 3;

        #endregion

        #region What fits the item

        /// <summary>A crafted weapon, armour piece, shield or instrument worth enchanting (level 15+, quality 94%+).</summary>
        public static bool IsWorthEnchanting(DbInventoryItem item) =>
            item?.Template != null && item.IsCrafted && !item.IsStackable && item.Level >= MinItemLevel && item.Quality >= MinQuality &&
            (GlobalConstants.IsArmor(item.Object_Type) || GlobalConstants.IsWeapon(item.Object_Type) ||
             item.Object_Type == (int)eObjectType.Instrument);

        /// <summary>Bonus slots still empty (SpellCrafting fills Bonus1..Bonus4 in order and refuses a full item).</summary>
        public static int FreeGemSlots(DbInventoryItem item)
        {
            if (item == null)
                return 0;
            int used = (item.Bonus1Type != 0 ? 1 : 0) + (item.Bonus2Type != 0 ? 1 : 0) + (item.Bonus3Type != 0 ? 1 : 0) +
                       (item.Bonus4Type != 0 ? 1 : 0);
            return MaxGemSlots - used;
        }

        /// <summary>AdvancedCraftingSkill.IsAllowedToCombine for a tincture: type 0 on armour, shields and instruments,
        /// type 1 on weapons (shields count as weapons there too); and Alchemy's rule: nothing on an item with a proc or charge.</summary>
        public static bool TinctureFits(DbInventoryItem item, int typeDamage)
        {
            if (item == null || item.ProcSpellID != 0 || item.SpellID != 0)
                return false;
            int type = item.Object_Type;
            if (type == (int)eObjectType.Instrument)
                return typeDamage == 0;
            return typeDamage switch
            {
                0 => GlobalConstants.IsArmor(type) || type == (int)eObjectType.Shield,
                1 => GlobalConstants.IsWeapon(type),
                _ => false,
            };
        }

        /// <summary>
        /// The bonuses a spellcrafter would put on this item, best first: the two main ones are fixed, the last two are a
        /// pair out of four sensible extras (variant 0-5), so listed pieces differ as players' do.
        /// </summary>
        public static int[] WantedBonuses(DbInventoryItem item, eRealm realm, int variant)
        {
            if (item == null)
                return [];
            int acuity = realm == eRealm.Midgard ? (int)eProperty.Piety : (int)eProperty.Intelligence;
            const int str = (int)eProperty.Strength, dex = (int)eProperty.Dexterity, con = (int)eProperty.Constitution,
                qui = (int)eProperty.Quickness, hp = (int)eProperty.MaxHealth, cha = (int)eProperty.Charisma;
            const int crush = (int)eProperty.Resist_Crush, slash = (int)eProperty.Resist_Slash, thrust = (int)eProperty.Resist_Thrust;
            int[] list = (eObjectType)item.Object_Type switch
            {
                // casters: cloth and staves
                eObjectType.Cloth or eObjectType.Staff => [acuity, dex, con, hp, slash, crush],
                eObjectType.Instrument => [cha, dex, con, qui, hp, slash],
                eObjectType.Longbow or eObjectType.CompositeBow or eObjectType.RecurvedBow or eObjectType.Crossbow or
                    eObjectType.Fired => [dex, qui, con, str, hp, thrust],
                _ when GlobalConstants.IsArmor(item.Object_Type) || item.Object_Type == (int)eObjectType.Shield =>
                    [str, con, dex, qui, hp, crush],
                _ => [str, qui, con, dex, hp, slash],   // melee weapons
            };
            // pairs out of the four extras (indexes 2..5)
            (int A, int B)[] pairs = [(2, 3), (2, 4), (3, 4), (2, 5), (3, 5), (4, 5)];
            (int a, int b) = pairs[Math.Abs(variant) % pairs.Length];
            return [list[0], list[1], list[a], list[b]];
        }

        #endregion

        #region Planning (pure: no money moved, nothing changed)

        /// <summary>
        /// The gems to put on the item: one per free slot, bonus types not already on it, each from a recipe the spellcrafter
        /// can make, the total imbue within the item's max (no overcharge) and the spellcrafter's cap, the materials plus the
        /// fee within the budget. Tiers are raised evenly (the lowest tier first, best bonus first on ties), as a
        /// spellcrafter spreads the points. Returns the gems best bonus first (empty when nothing fits).
        /// </summary>
        public static List<GemOption> PickGems(DbInventoryItem item, IReadOnlyList<int> wanted,
            IReadOnlyDictionary<int, GemOption[]> gemsByType, int spellcraftSkill, long budget)
        {
            var picked = new List<GemOption>();
            int slots = FreeGemSlots(item);
            if (item == null || wanted == null || gemsByType == null || slots <= 0 || budget <= 0)
                return picked;
            int limit = Math.Min(ItemMaxImbue(item), SpellcrafterCap(spellcraftSkill));
            if (limit <= 0)
                return picked;

            var present = new HashSet<int> { item.Bonus1Type, item.Bonus2Type, item.Bonus3Type, item.Bonus4Type };
            var types = new List<GemOption[]>();
            foreach (int type in wanted)
            {
                if (types.Count >= slots)
                    break;
                if (type == 0 || present.Contains(type) || !gemsByType.TryGetValue(type, out GemOption[] all))
                    continue;
                GemOption[] makeable = all.Where(g => CanMake(spellcraftSkill, g.Level)).OrderBy(g => g.BonusValue).ToArray();
                if (makeable.Length == 0)
                    continue;
                present.Add(type);
                types.Add(makeable);
            }
            if (types.Count == 0)
                return picked;

            int[] tier = Enumerable.Repeat(-1, types.Count).ToArray();
            bool raised = true;
            while (raised)
            {
                raised = false;
                foreach (int i in Enumerable.Range(0, types.Count).OrderBy(i => tier[i]).ThenBy(i => i))
                {
                    if (tier[i] + 1 >= types[i].Length)
                        continue;
                    tier[i]++;
                    if (Fits(item, types, tier, limit, budget))
                    {
                        raised = true;
                        break;
                    }
                    tier[i]--;
                }
            }
            for (int i = 0; i < types.Count; i++)
                if (tier[i] >= 0)
                    picked.Add(types[i][tier[i]]);
            return picked;
        }

        private static bool Fits(DbInventoryItem item, List<GemOption[]> types, int[] tier, int limit, long budget)
        {
            var gems = new List<GemOption>(types.Count);
            for (int i = 0; i < types.Count; i++)
                if (tier[i] >= 0)
                    gems.Add(types[i][tier[i]]);
            long materials = gems.Sum(g => g.Cost);
            return TotalImbue(item, gems.Select(g => (g.BonusType, g.BonusValue))) <= limit && materials + Fee(materials) <= budget;
        }

        /// <summary>
        /// The tincture to put on the item: one that fits it (server combine rules), from a recipe the alchemist can make,
        /// whose level requirement is not above the item's level (it stays wearable at its level), materials plus fee within
        /// the budget. A proc beats charges (a weapon proc on weapons, an armour proc on armour, shields and instruments);
        /// then the highest level requirement; ties broken by the roll (any non-negative number).
        /// </summary>
        public static TinctureOption PickTincture(DbInventoryItem item, IReadOnlyList<TinctureOption> tinctures, int alchemySkill,
            long budget, int roll)
        {
            if (item == null || tinctures == null || budget <= 0)
                return null;
            bool weapon = GlobalConstants.IsWeapon(item.Object_Type) && item.Object_Type != (int)eObjectType.Shield;
            TinctureKind preferred = weapon ? TinctureKind.WeaponProc : TinctureKind.ArmourProc;
            TinctureOption[] fitting = tinctures
                .Where(t => TinctureFits(item, t.TypeDamage) && t.LevelRequirement <= item.Level && CanMake(alchemySkill, t.Level) &&
                            t.Cost + Fee(t.Cost) <= budget)
                .ToArray();
            if (fitting.Length == 0)
                return null;
            int Rank(TinctureOption t) => t.Kind == preferred ? 2 : t.Kind == TinctureKind.Charges ? 0 : 1;
            int bestRank = fitting.Max(Rank);
            TinctureOption[] best = fitting.Where(t => Rank(t) == bestRank).ToArray();
            int bestLevel = best.Max(t => t.LevelRequirement);
            best = best.Where(t => t.LevelRequirement == bestLevel).OrderBy(t => t.RecipeId).ToArray();
            return best[Math.Abs(roll) % best.Length];
        }

        /// <summary>The enchanter's fee: 10% of the materials (at least 1 copper when there are materials).</summary>
        public static long Fee(long materials) => materials <= 0 ? 0 : Math.Max(1, materials * FeePercent / 100);

        /// <summary>Chance (percent) a crafter has a fresh piece enchanted: 15% plus 1% per 5 gold it holds, at most 75%.</summary>
        public static int EnchantChance(long money) => (int)Math.Clamp(15 + Math.Max(0, money) / 50_000, 15, 75);

        #endregion

        #region Applying (exactly as the player combines do)

        /// <summary>SpellCrafting.ApplySpellcraftGems (success): each gem's bonus into the next empty slot.</summary>
        public static void ApplyGems(DbInventoryItem item, IEnumerable<GemOption> gems)
        {
            foreach (GemOption gem in gems)
            {
                if (item.Bonus1Type == 0)
                {
                    item.Bonus1Type = gem.BonusType;
                    item.Bonus1 = gem.BonusValue;
                }
                else if (item.Bonus2Type == 0)
                {
                    item.Bonus2Type = gem.BonusType;
                    item.Bonus2 = gem.BonusValue;
                }
                else if (item.Bonus3Type == 0)
                {
                    item.Bonus3Type = gem.BonusType;
                    item.Bonus3 = gem.BonusValue;
                }
                else if (item.Bonus4Type == 0)
                {
                    item.Bonus4Type = gem.BonusType;
                    item.Bonus4 = gem.BonusValue;
                }
            }
        }

        /// <summary>Alchemy.ApplyMagicalEffect: a proc (and its chance), or charges by quality and the spell; the tincture's
        /// level requirement.</summary>
        public static void ApplyTincture(DbInventoryItem item, TinctureOption tincture)
        {
            if (tincture.ProcSpellId != 0)
            {
                item.ProcSpellID = tincture.ProcSpellId;
                if (tincture.ProcChance != 0)
                    item.ProcChance = tincture.ProcChance;
            }
            else
            {
                item.MaxCharges = MaxCharges(item);
                item.Charges = item.MaxCharges;
                item.SpellID = tincture.SpellId;
            }
            item.LevelRequirement = tincture.LevelRequirement;
        }

        #endregion

        #region Recipes (once per realm)

        private sealed class Catalog
        {
            public Dictionary<int, GemOption[]> Gems = new();
            public TinctureOption[] Tinctures = [];
        }

        private static readonly ConcurrentDictionary<eRealm, Catalog> Catalogs = new();

        private static Catalog CatalogOf(eRealm realm) => Catalogs.GetOrAdd(realm, Load);

        private static Catalog Load(eRealm realm)
        {
            var catalog = new Catalog();
            // the real recipe lists the crafting assistant uses (whole-pack merchant material costs); only buyable ones
            var gems = new Dictionary<(int Type, int Value), GemOption>();
            foreach (CraftAssist165Economics.RecipeInfo info in CraftAssist165Economics.RecipesOf(eCraftingSkill.SpellCrafting, realm))
            {
                DbItemTemplate product = info.Recipe?.Product;
                if (product == null || !info.Buyable || product.Object_Type != (int)eObjectType.SpellcraftGem ||
                    product.Bonus1Type == 0 || product.Bonus1 <= 0)
                    continue;
                var gem = new GemOption(info.Id, product.Name, product.Bonus1Type, product.Bonus1, info.Level, info.MaterialCost);
                if (!gems.TryGetValue((gem.BonusType, gem.BonusValue), out GemOption old) || gem.Cost < old.Cost)
                    gems[(gem.BonusType, gem.BonusValue)] = gem;
            }
            catalog.Gems = gems.Values.GroupBy(g => g.BonusType).ToDictionary(g => g.Key, g => g.OrderBy(x => x.BonusValue).ToArray());

            var tinctures = new List<TinctureOption>();
            foreach (CraftAssist165Economics.RecipeInfo info in CraftAssist165Economics.RecipesOf(eCraftingSkill.Alchemy, realm))
            {
                DbItemTemplate product = info.Recipe?.Product;
                if (product == null || !info.Buyable || product.Object_Type != (int)eObjectType.AlchemyTincture ||
                    product.ProcSpellID == 0 && product.SpellID == 0)
                    continue;
                tinctures.Add(new TinctureOption(info.Id, product.Name, product.Type_Damage, product.ProcSpellID, product.ProcChance,
                    product.SpellID, product.LevelRequirement, info.Level, info.MaterialCost));
            }
            catalog.Tinctures = tinctures.ToArray();
            return catalog;
        }

        #endregion

        #region The realm's best enchanters (refreshed every few minutes)

        private sealed record Enchanters(long Time, long SpellcrafterId, long AlchemistId);

        private static readonly ConcurrentDictionary<eRealm, Enchanters> Best = new();
        private static readonly Lock RefreshLock = new();

        /// <summary>The best live Spellcrafting and Alchemy gamebots of the realm (null when none), from a list refreshed
        /// every five minutes in one pass over the live bots.</summary>
        private static (GameBot Spellcrafter, GameBot Alchemist) FindEnchanters(eRealm realm, long now)
        {
            if (!Best.TryGetValue(realm, out Enchanters best) || now >= best.Time + RefreshMilliseconds)
            {
                lock (RefreshLock)
                {
                    if (!Best.TryGetValue(realm, out best) || now >= best.Time + RefreshMilliseconds)
                    {
                        Refresh(now);
                        Best.TryGetValue(realm, out best);
                    }
                }
            }
            return best == null ? (null, null) : (Live(best.SpellcrafterId, eCraftingSkill.SpellCrafting), Live(best.AlchemistId, eCraftingSkill.Alchemy));
        }

        private static void Refresh(long now)
        {
            var spell = new Dictionary<eRealm, (long Id, int Level)>();
            var alch = new Dictionary<eRealm, (long Id, int Level)>();
            foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            {
                // only bots already given their craft; hired companions are never crafters
                if (bot?.PersistentRecord is not { } record || record.CraftLevel <= 0 || HiredCompanions165.IsHired(record.BotId))
                    continue;
                var table = record.CraftSkill == (int)eCraftingSkill.SpellCrafting ? spell
                    : record.CraftSkill == (int)eCraftingSkill.Alchemy ? alch : null;
                if (table != null && (!table.TryGetValue(bot.Realm, out var top) || record.CraftLevel > top.Level))
                    table[bot.Realm] = (bot.DatabaseID, record.CraftLevel);
            }
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                Best[realm] = new Enchanters(now, spell.TryGetValue(realm, out var s) ? s.Id : 0, alch.TryGetValue(realm, out var a) ? a.Id : 0);
        }

        private static GameBot Live(long botId, eCraftingSkill craft) =>
            botId > 0 && AutonomousBotRegistry.TryGet(botId, out GameBot bot) && bot?.PersistentRecord is { } record &&
            record.CraftSkill == (int)craft && !HiredCompanions165.IsHired(botId) ? bot : null;

        /// <summary>Forget the cached recipes and enchanters (tests, database reloads).</summary>
        public static void ClearCache()
        {
            Catalogs.Clear();
            Best.Clear();
        }

        #endregion

        /// <summary>
        /// A crafter bot's fresh piece (before it is listed): maybe have it enchanted by the realm's best spellcrafter and
        /// alchemist bots, paying materials and fees. The item must be a unique copy (its bonuses are saved with it).
        /// At most one attempt per item. Returns what was done (Done false when nothing).
        /// </summary>
        public static Result TryEnchant(GameBot crafter, DbInventoryItem item)
        {
            var result = new Result();
            if (!Classic165Mode.IsOn || crafter?.PersistentRecord == null || item?.Template is not DbItemUnique ||
                !IsWorthEnchanting(item))
                return result;
            long money = AutonomousBotEconomy.GetMoney(crafter.DatabaseID);
            if (!Util.Chance(EnchantChance(money)) || !AutonomousBotEconomy.CanListOnExchange(crafter))
                return result;
            (GameBot spellcrafter, GameBot alchemist) = FindEnchanters(crafter.Realm, GameLoop.GameLoopTime);
            if (spellcrafter == null && alchemist == null)
                return result;

            // at most half of its coin: the rest buys its next materials
            long budget = money / 2;
            Catalog catalog = CatalogOf(crafter.Realm);
            int spellSkill = spellcrafter?.PersistentRecord.CraftLevel ?? 0;
            int alchSkill = alchemist?.PersistentRecord.CraftLevel ?? 0;
            List<GemOption> gems = spellcrafter == null ? new List<GemOption>()
                : PickGems(item, WantedBonuses(item, crafter.Realm, Util.Random(5)), catalog.Gems, spellSkill, budget);
            long gemMaterials = gems.Sum(g => g.Cost);
            long left = budget - gemMaterials - Fee(gemMaterials);
            TinctureOption tincture = alchemist == null ? null : PickTincture(item, catalog.Tinctures, alchSkill, left, Util.Random(1000));
            if (gems.Count == 0 && tincture == null)
                return result;

            long tinctureMaterials = tincture?.Cost ?? 0;
            long spellFee = Fee(gemMaterials), alchFee = Fee(tinctureMaterials);
            long total = gemMaterials + tinctureMaterials + spellFee + alchFee;
            if (!AutonomousBotEconomy.TrySpend(crafter.DatabaseID, total))
                return result;

            int imbue = TotalImbue(item, gems.Select(g => (g.BonusType, g.BonusValue)));
            if (gems.Count > 0)
            {
                ApplyGems(item, gems);
                AutonomousBotEconomy.AddMoney(spellcrafter.DatabaseID, spellFee);
                foreach (GemOption gem in gems)
                    CrafterBots165.Gain(spellcrafter.PersistentRecord, gem.Level - spellSkill);
                spellcrafter.MarkAutonomousStateDirty();
            }
            if (tincture != null)
            {
                ApplyTincture(item, tincture);
                AutonomousBotEconomy.AddMoney(alchemist.DatabaseID, alchFee);
                CrafterBots165.Gain(alchemist.PersistentRecord, tincture.Level - alchSkill);
                alchemist.MarkAutonomousStateDirty();
            }

            result.Gems = gems;
            result.Tincture = tincture;
            result.Materials = gemMaterials + tinctureMaterials;
            result.SpellFee = gems.Count > 0 ? spellFee : 0;
            result.AlchemyFee = tincture != null ? alchFee : 0;
            string bonuses = gems.Count == 0 ? "none" : string.Join(",", gems.Select(g => $"{(eProperty)g.BonusType}+{g.BonusValue}"));
            string proc = tincture == null ? "none" : $"{tincture.Kind}:{(tincture.ProcSpellId != 0 ? tincture.ProcSpellId : tincture.SpellId)}" +
                (tincture.Kind == TinctureKind.Charges ? $"x{item.MaxCharges}" : string.Empty) + $"(lr{tincture.LevelRequirement})";
            Log.Info($"BOT_ENCHANTED_165 crafter={crafter.Name} id={crafter.DatabaseID} " +
                     $"spellcrafter={(gems.Count > 0 ? $"{spellcrafter.Name}({spellSkill})" : "none")} " +
                     $"alchemist={(tincture != null ? $"{alchemist.Name}({alchSkill})" : "none")} item=\"{item.Name}\" level={item.Level} " +
                     $"quality={item.Quality} bonuses={bonuses} imbue={imbue}/{ItemMaxImbue(item)} proc={proc} " +
                     $"cost={result.Cost} materials={result.Materials} fees={result.SpellFee + result.AlchemyFee}");
            return result;
        }
    }
}
