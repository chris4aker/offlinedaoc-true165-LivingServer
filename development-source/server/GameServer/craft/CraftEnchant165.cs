using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>Op 44 state byte.</summary>
    public enum EnchantState : byte { Idle = 0, Buying = 1, Crafting = 2, Combining = 3, Done = 4, Stopped = 5 }

    /// <summary>Op 44 reason byte.</summary>
    public enum EnchantReason : byte
    {
        None = 0, Done = 1, OutOfMoney = 2, BackpackFull = 3, Interrupted = 4, NotEnoughSkill = 5, ItemGone = 6,
        StoppedByPlayer = 7, CombineFailed = 8, Other = 9,
    }

    /// <summary>Op 43 header reason byte (why the item can't be enchanted).</summary>
    public enum EnchantBlock : byte { None = 0, NotAllowed = 1, TooLowLevel = 2, NoSlotsHasProc = 3, NoSkill = 4 }

    /// <summary>Op 43 tincture row kind.</summary>
    public enum TinctureKind : byte { None = 0, WeaponProc = 1, ArmourProc = 2, Charges = 3 }

    /// <summary>One gem the player picked (pure data, so the plan is testable).</summary>
    public sealed class EnchantGemChoice
    {
        public ushort RecipeId;
        public int BonusType, BonusValue;
        public int RequiredLevel;           // the recipe's crafting level
        public long MaterialCost;           // missing materials now, merchant price
        public int Seconds;                 // normal player craft time
        public int Owned;                   // matching gems in the backpack
        public int OwnedQuality;            // the best owned one's quality
        public string Name;
        public Recipe Recipe;               // game side
    }

    /// <summary>The tincture the player picked (pure data).</summary>
    public sealed class EnchantTinctureChoice
    {
        public ushort RecipeId;
        public int TypeDamage, ProcSpellID, SpellID, ProcChance, LevelRequirement;
        public int RequiredLevel;
        public long MaterialCost;
        public int Seconds;
        public int Owned;
        public string Name;
        public Recipe Recipe;
    }

    /// <summary>The checked plan of an enchant: what is used from the backpack, what is crafted, the imbue numbers.</summary>
    public sealed class EnchantPlan
    {
        public bool Ok;
        public EnchantReason Reason;
        public string Why;
        public int Total, Max, Cap, Over;
        public int SuccessChance = 100;     // overcharge success (100 when not over)
        public int PreserveChance = 100;    // CalculateChanceToPreserveItem: rolled only when the overcharge fails
        public readonly List<int> Owned = new();    // choice indexes taken from the backpack
        public readonly List<int> Crafted = new();  // choice indexes to craft
        public long MaterialCost;
        public int Seconds;

        /// <summary>The chance the gems explode: the overcharge fails and the preserve roll fails too.</summary>
        public int ExplodeChance => Over > 0 ? (int)Math.Round((100 - SuccessChance) * (100 - PreserveChance) / 100.0) : 0;

        public static EnchantPlan Fail(EnchantReason reason, string why) => new() { Ok = false, Reason = reason, Why = why };
    }

    /// <summary>The op 43 header numbers of one item.</summary>
    public sealed class EnchantHeader
    {
        public int Slot, Level, Quality, FreeSlots;
        public bool HasProc, CanSpell, CanAlchemy;
        public int ImbueMax, ImbueUsed, SpellSkill, AlchemySkill, Cap;
        public EnchantBlock Reason;
        public string Name = string.Empty;
    }

    public readonly record struct EnchantGemRow(ushort RecipeId, int BonusType, int BonusValue, int ImbueTenths, long MaterialCost,
        int Seconds, int RequiredLevel, int Owned, bool CanCraft, int Quality);

    public readonly record struct EnchantTinctureRow(ushort RecipeId, TinctureKind Kind, int Charges, int SpellId, int LevelRequirement,
        int Owned, long MaterialCost, int Seconds, int RequiredLevel, bool CanCraft, int ProcChance);

    /// <summary>One gem / tincture the run still has to craft.</summary>
    public sealed class EnchantCraft
    {
        public ushort RecipeId;
        public Recipe Recipe;
        public string Name;
        public double ExpectedSeconds;      // craft time / make chance (failures take the full time and are retried)
    }

    /// <summary>The data of one enchant run (op 44 fields + the game side).</summary>
    public sealed class EnchantRun
    {
        public readonly object Sync = new();
        public EnchantState State = EnchantState.Idle;
        public EnchantReason Reason;
        public bool ItemDestroyed, GemsLost;
        public bool Alchemy, AllowOvercharge;
        public int StepIndex, StepsTotal;
        public long Spent;
        public ushort RecipeId;
        public double SecondsLeft;
        public string ItemName = string.Empty;
        // game side
        public DbInventoryItem Item;
        public List<EnchantCraft> Crafts = new();
        public int NextCraft;
        public List<DbInventoryItem> Ready = new();     // gems / tincture to combine (owned ones and the ones just made)
        public ushort Region;
        public int X, Y;
        public ECSGameTimer Timer;

        public bool Running => State is EnchantState.Buying or EnchantState.Crafting or EnchantState.Combining;

        public void Begin(bool alchemy, bool allowOvercharge, string itemName)
        {
            State = EnchantState.Buying; Reason = EnchantReason.None;
            ItemDestroyed = GemsLost = false;
            Alchemy = alchemy; AllowOvercharge = allowOvercharge;
            StepIndex = StepsTotal = 0; Spent = 0; RecipeId = 0; SecondsLeft = 0;
            ItemName = itemName ?? string.Empty;
            Item = null; Crafts = new(); NextCraft = 0; Ready = new();
        }

        /// <summary>Ends a running run; false if it was not running (so the end is reported once).</summary>
        public bool End(EnchantReason reason)
        {
            if (!Running)
                return false;
            State = reason == EnchantReason.Done ? EnchantState.Done : EnchantState.Stopped;
            Reason = reason;
            RecipeId = 0;
            SecondsLeft = 0;
            if (reason == EnchantReason.Done)
                StepIndex = StepsTotal;
            return true;
        }

        /// <summary>Expected seconds of the crafts not made yet.</summary>
        public void UpdateSecondsLeft()
        {
            double s = 0;
            for (int i = NextCraft; i < Crafts.Count; i++)
                s += Crafts[i].ExpectedSeconds;
            SecondsLeft = s;
        }
    }

    /// <summary>
    /// True 1.65 enchant helper (owner 2026-11-08, quality of life): puts spellcraft gems or an alchemy tincture on a
    /// crafted item in the backpack. Gems / tinctures already in the backpack are used first; the rest are crafted with
    /// the player's own skill through the normal craft path (normal timer, GMs too; real materials bought at merchant
    /// prices, never free), then combined with the real SpellCrafting / Alchemy combine (normal rolls, messages and
    /// saving) without a trade window. Protocol: enchant_helper_spec (op 43 info, op 44 status).
    /// </summary>
    public static class CraftEnchant165
    {
        public const byte OpEnchantInfo = 43, OpEnchantStatus = 44;
        public const byte KindHeader = 0, KindGems = 1, KindTinctures = 2, KindStrings = 3, KindImbueRules = 4, KindEnd = 0xFF;
        /// <summary>Rows per op 43 row packet (2 + 5 x 20 = 102 bytes).</summary>
        public const int RowsPerPacket = 5;
        /// <summary>Payload bytes we fill at most (the packet carries 123 from body [4]).</summary>
        public const int MaxPayload = 120;
        public const int MaxNameBytes = 96;
        /// <summary>IsAllowedToCombine refuses more than this many points over the item's imbue points.</summary>
        public const int MaxOvercharge = 6;

        private static readonly ConcurrentDictionary<GamePlayer, EnchantRun> Runs = new();

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            GameEventMgr.AddHandler(GamePlayerEvent.Quit, new DOLEventHandler(OnPlayerQuit));
            GameEventMgr.AddHandler(GamePlayerEvent.RegionChanged, new DOLEventHandler(OnPlayerLeft));
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            GameEventMgr.RemoveHandler(GamePlayerEvent.Quit, new DOLEventHandler(OnPlayerQuit));
            GameEventMgr.RemoveHandler(GamePlayerEvent.RegionChanged, new DOLEventHandler(OnPlayerLeft));
        }

        // a quitting player's run is dropped without a word
        private static void OnPlayerQuit(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is not GamePlayer player || !Runs.TryRemove(player, out EnchantRun run))
                return;
            lock (run.Sync)
            {
                run.End(EnchantReason.Interrupted);
                run.Timer?.Stop();
                run.Timer = null;
            }
        }

        private static void OnPlayerLeft(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is GamePlayer player && IsRunning(player))
                Stop(player, EnchantReason.Interrupted, null);
        }

        public static bool IsRunning(GamePlayer player) => player != null && Runs.TryGetValue(player, out var r) && r.Running;

        #region Pure 1.65 rules (mirrors of AdvancedCraftingSkill / SpellCrafting / Alchemy IsAllowedToCombine)

        /// <summary>Items that can take gems or a tincture: weapons (shields too), armour and instruments.</summary>
        public static bool IsEnchantableType(int objectType) =>
            GlobalConstants.IsArmor(objectType) || GlobalConstants.IsWeapon(objectType) || objectType == (int)eObjectType.Instrument;

        public static bool IsEnchantable(DbInventoryItem item) => item != null && item.IsCrafted && IsEnchantableType(item.Object_Type);

        /// <summary>Bonuses already on the item, counted in order as SpellCrafting.IsAllowedToCombine counts them.</summary>
        public static int ExistingBonusCount(DbInventoryItem item)
        {
            if (item.Bonus1Type == 0) return 0;
            if (item.Bonus2Type == 0) return 1;
            if (item.Bonus3Type == 0) return 2;
            if (item.Bonus4Type == 0) return 3;
            return 4;
        }

        /// <summary>Gem slots left: one gem per bonus, four bonuses on an item.</summary>
        public static int FreeBonusSlots(DbInventoryItem item) => 4 - ExistingBonusCount(item);

        /// <summary>The item's bonus types a gem may not repeat (Bonus1-3 in order, as IsAllowedToCombine collects them).</summary>
        public static List<int> ExistingBonusTypes(DbInventoryItem item)
        {
            var types = new List<int>(3);
            if (item.Bonus1Type != 0)
            {
                types.Add(item.Bonus1Type);
                if (item.Bonus2Type != 0)
                {
                    types.Add(item.Bonus2Type);
                    if (item.Bonus3Type != 0)
                        types.Add(item.Bonus3Type);
                }
            }
            return types;
        }

        /// <summary>Raw imbue points of the item's own bonuses and the biggest one (what TotalImbuePoints adds).</summary>
        public static (int Sum, int Biggest) ExistingImbue(DbInventoryItem item)
        {
            int sum = 0, biggest = 0;
            var bonuses = new List<(int, int)>(3);
            if (item.Bonus1Type != 0)
            {
                bonuses.Add((item.Bonus1Type, item.Bonus1));
                if (item.Bonus2Type != 0)
                {
                    bonuses.Add((item.Bonus2Type, item.Bonus2));
                    if (item.Bonus3Type != 0)
                        bonuses.Add((item.Bonus3Type, item.Bonus3));
                }
            }
            foreach (var (type, value) in bonuses)
            {
                int p = SpellCrafting.GemImbuePoints(type, value);
                sum += p;
                if (p > biggest)
                    biggest = p;
            }
            return (sum, biggest);
        }

        public static bool HasProcOrCharge(DbInventoryItem item) => item.ProcSpellID != 0 || item.SpellID != 0;

        /// <summary>AdvancedCraftingSkill.IsAllowedToCombine's tincture rule: Type_Damage 0 = armour (shields and
        /// instruments too), 1 = weapons (shields count as weapons); instruments take only Type_Damage 0.</summary>
        public static bool TinctureFits(int itemObjectType, int typeDamage)
        {
            if (itemObjectType == (int)eObjectType.Instrument)
                return typeDamage == 0;
            switch (typeDamage)
            {
                case 0:
                    return GlobalConstants.IsArmor(itemObjectType) || itemObjectType == (int)eObjectType.Shield;
                case 1:
                    return GlobalConstants.IsWeapon(itemObjectType);
                default:
                    return false;
            }
        }

        /// <summary>Alchemy.ApplyMagicalEffect: a ProcSpellID makes a proc, otherwise SpellID makes charges.</summary>
        public static TinctureKind KindOf(int typeDamage, int procSpellId, int spellId)
        {
            if (procSpellId != 0)
                return typeDamage == 1 ? TinctureKind.WeaponProc : TinctureKind.ArmourProc;
            return spellId != 0 ? TinctureKind.Charges : TinctureKind.None;
        }

        /// <summary>Overcharge success: the real formula when over the item's points, else 100 (no roll is made).</summary>
        public static int SuccessChance(int itemQuality, int spellSkill, int max, int total) =>
            total > max ? SpellCrafting.OverchargeSuccessChance(itemQuality, spellSkill, max, total) : 100;

        /// <summary>The lowest quality a newly crafted gem / tincture can have (they are unique products: GetQuality).</summary>
        public static int CraftedQualityFloor => Classic165Mode.IsOn ? 94 : 96;

        /// <summary>AbstractCraftingSkill.GetItemCon below purple: the player's own skill can make this recipe.</summary>
        public static bool CanMake(int skill, int recipeLevel) => CraftAssist165Economics.ItemCon(skill, recipeLevel) < 3;

        /// <summary>Is this backpack item the same gem / tincture as the product (crafted ones are uniques with new ids,
        /// so gems match by bonus, tinctures by their effect)?</summary>
        public static bool SameEnchantItem(DbInventoryItem item, DbItemTemplate product)
        {
            if (item == null || product == null || item.Object_Type != product.Object_Type)
                return false;
            if (product.Object_Type == (int)eObjectType.SpellcraftGem)
                return item.Bonus1Type == product.Bonus1Type && item.Bonus1 == product.Bonus1;
            if (product.Object_Type == (int)eObjectType.AlchemyTincture)
                return item.Type_Damage == product.Type_Damage && item.ProcSpellID == product.ProcSpellID && item.SpellID == product.SpellID &&
                    item.ProcChance == product.ProcChance && item.LevelRequirement == product.LevelRequirement;
            return false;
        }

        #endregion

        #region Plans (pure)

        /// <summary>
        /// Checks a spellcraft enchant the way SpellCrafting.IsAllowedToCombine will, plus the helper's own rules: owned
        /// gems are used first, every other gem must be makeable with the player's own skill, the total within the
        /// spellcrafter cap (skill / 20), overcharging only when allowed and only while it can succeed.
        /// craftedGemQuality = the quality assumed for gems still to craft (the lowest possible: a cautious risk).
        /// </summary>
        public static EnchantPlan PlanSpell(DbInventoryItem item, IReadOnlyList<EnchantGemChoice> gems, int spellSkill, bool allowOvercharge, int craftedGemQuality)
        {
            if (!IsEnchantable(item))
                return EnchantPlan.Fail(EnchantReason.ItemGone, "only crafted weapons, armour and instruments can be enchanted.");
            if (spellSkill <= 0)
                return EnchantPlan.Fail(EnchantReason.NotEnoughSkill, "you have no Spellcrafting skill.");
            if (gems == null || gems.Count == 0)
                return EnchantPlan.Fail(EnchantReason.Other, "pick at least one gem.");
            int free = FreeBonusSlots(item);
            if (free <= 0)
                return EnchantPlan.Fail(EnchantReason.ItemGone, $"the {item.Name} has no free bonus slot.");
            if (gems.Count > free)
                return EnchantPlan.Fail(EnchantReason.Other, $"the {item.Name} has only {free} free bonus slot{(free == 1 ? "" : "s")}.");

            List<int> types = ExistingBonusTypes(item);
            foreach (EnchantGemChoice gem in gems)
            {
                if (gem.BonusType == 0 || types.Contains(gem.BonusType))
                    return EnchantPlan.Fail(EnchantReason.Other, "each bonus can be on the item only once.");
                types.Add(gem.BonusType);
            }

            var plan = new EnchantPlan();
            for (int i = 0; i < gems.Count; i++)
            {
                EnchantGemChoice gem = gems[i];
                if (gem.Owned > 0)
                {
                    plan.Owned.Add(i);
                    continue;
                }
                if (!CanMake(spellSkill, gem.RequiredLevel))
                    return EnchantPlan.Fail(EnchantReason.NotEnoughSkill,
                        $"your Spellcrafting ({spellSkill}) is too low to make the {gem.Name ?? "gem"} (needs {gem.RequiredLevel - 49}).");
                plan.Crafted.Add(i);
                plan.MaterialCost += gem.MaterialCost;
                plan.Seconds += gem.Seconds;
            }

            plan.Total = SpellCrafting.TotalImbuePoints(item, gems.Select(g => (g.BonusType, g.BonusValue)));
            plan.Max = SpellCrafting.ItemMaxImbuePoints(item.Level, item.Quality);
            plan.Cap = spellSkill / 20;
            plan.Over = plan.Total - plan.Max;
            if (plan.Total > plan.Cap)
                return EnchantPlan.Fail(EnchantReason.NotEnoughSkill,
                    $"{plan.Total} imbue points is over your spellcrafting cap of {plan.Cap} (skill / 20).");
            if (plan.Over > 0)
            {
                if (!allowOvercharge)
                    return EnchantPlan.Fail(EnchantReason.Other,
                        $"{plan.Total} imbue points is over the item's {plan.Max}: tick allow overcharge to try it.");
                plan.SuccessChance = SuccessChance(item.Quality, spellSkill, plan.Max, plan.Total);
                if (plan.Over > MaxOvercharge || plan.SuccessChance <= 0)
                    return EnchantPlan.Fail(EnchantReason.Other,
                        $"{plan.Total} imbue points is too far over the item's {plan.Max}: the overcharge can't succeed.");
            }
            plan.PreserveChance = SpellCrafting.PreserveChance(item.Quality,
                gems.Select(g => g.Owned > 0 ? g.OwnedQuality : craftedGemQuality), spellSkill);
            plan.Ok = true;
            return plan;
        }

        /// <summary>Checks an alchemy enchant the way Alchemy.IsAllowedToCombine will, plus the own-skill rule.</summary>
        public static EnchantPlan PlanAlchemy(DbInventoryItem item, EnchantTinctureChoice tincture, int alchemySkill)
        {
            if (!IsEnchantable(item))
                return EnchantPlan.Fail(EnchantReason.ItemGone, "only crafted weapons, armour and instruments can be enchanted.");
            if (HasProcOrCharge(item))
                return EnchantPlan.Fail(EnchantReason.ItemGone, $"the {item.Name} already has a proc or charges.");
            if (alchemySkill <= 0)
                return EnchantPlan.Fail(EnchantReason.NotEnoughSkill, "you have no Alchemy skill.");
            if (tincture == null || KindOf(tincture.TypeDamage, tincture.ProcSpellID, tincture.SpellID) == TinctureKind.None)
                return EnchantPlan.Fail(EnchantReason.Other, "pick a tincture.");
            if (!TinctureFits(item.Object_Type, tincture.TypeDamage))
                return EnchantPlan.Fail(EnchantReason.Other, $"that tincture does not fit the {item.Name}.");

            var plan = new EnchantPlan();
            if (tincture.Owned > 0)
                plan.Owned.Add(0);
            else
            {
                if (!CanMake(alchemySkill, tincture.RequiredLevel))
                    return EnchantPlan.Fail(EnchantReason.NotEnoughSkill,
                        $"your Alchemy ({alchemySkill}) is too low to make the {tincture.Name ?? "tincture"} (needs {tincture.RequiredLevel - 49}).");
                plan.Crafted.Add(0);
                plan.MaterialCost = tincture.MaterialCost;
                plan.Seconds = tincture.Seconds;
            }
            plan.Ok = true;
            return plan;
        }

        /// <summary>The op 43 header of an item (null item: not enchantable).</summary>
        public static EnchantHeader HeaderOf(DbInventoryItem item, int slot, int spellSkill, int alchemySkill)
        {
            var h = new EnchantHeader { Slot = slot, SpellSkill = spellSkill, AlchemySkill = alchemySkill, Cap = spellSkill / 20 };
            if (item == null)
            {
                h.Reason = EnchantBlock.NotAllowed;
                return h;
            }
            h.Name = item.Name ?? string.Empty;
            h.Level = item.Level;
            h.Quality = item.Quality;
            h.FreeSlots = FreeBonusSlots(item);
            h.HasProc = HasProcOrCharge(item);
            h.ImbueMax = SpellCrafting.ItemMaxImbuePoints(item.Level, item.Quality);
            h.ImbueUsed = SpellCrafting.TotalImbuePoints(item, Array.Empty<(int, int)>());
            if (!IsEnchantable(item))
            {
                h.Reason = EnchantBlock.NotAllowed;
                return h;
            }
            bool fits = TinctureFits(item.Object_Type, 0) || TinctureFits(item.Object_Type, 1);
            h.CanSpell = h.FreeSlots > 0 && spellSkill > 0;
            h.CanAlchemy = !h.HasProc && alchemySkill > 0 && fits;
            // an item that holds no imbue points at all (very low level) can only be overcharged: not offered
            if (h.CanSpell && h.ImbueMax <= 0 && !h.CanAlchemy)
            {
                h.CanSpell = false;
                h.Reason = EnchantBlock.TooLowLevel;
                return h;
            }
            if (!h.CanSpell && !h.CanAlchemy)
                h.Reason = h.FreeSlots <= 0 && h.HasProc ? EnchantBlock.NoSlotsHasProc : EnchantBlock.NoSkill;
            return h;
        }

        /// <summary>What buying the missing materials of these ingredients costs now (whole packs, merchant price).</summary>
        public static long MissingCost(IReadOnlyDictionary<string, int> have, IEnumerable<Ingredient> ingredients)
        {
            var need = new Dictionary<string, (int Count, DbItemTemplate Material)>();
            foreach (Ingredient ingredient in ingredients)
            {
                if (ingredient.Material == null)
                    continue;
                string name = ingredient.Material.Name;
                need[name] = need.TryGetValue(name, out var n) ? (n.Count + ingredient.Count, n.Material) : (ingredient.Count, ingredient.Material);
            }
            long total = 0;
            foreach (var kv in need)
            {
                int missing = kv.Value.Count - (have != null && have.TryGetValue(kv.Key, out int c) ? c : 0);
                total += CraftAssist165Economics.PackCost(kv.Value.Material.Price, kv.Value.Material.PackSize, missing);
            }
            return total;
        }

        /// <summary>Every ingredient is sold by merchants, or the backpack already has enough of it.</summary>
        public static bool MaterialsObtainable(IReadOnlyDictionary<string, int> have, IEnumerable<Ingredient> ingredients)
        {
            var need = new Dictionary<string, (int Count, long Price)>();
            foreach (Ingredient ingredient in ingredients)
            {
                if (ingredient.Material == null)
                    return false;
                string name = ingredient.Material.Name;
                need[name] = need.TryGetValue(name, out var n) ? (n.Count + ingredient.Count, n.Price) : (ingredient.Count, ingredient.Material.Price);
            }
            foreach (var kv in need)
                if (kv.Value.Price <= 0 && (have == null || !have.TryGetValue(kv.Key, out int c) || c < kv.Value.Count))
                    return false;
            return true;
        }

        /// <summary>Missing lines of several crafts merged per material (so packs are counted once).</summary>
        public static List<(DbItemTemplate Material, int Need)> MergeMissing(IEnumerable<(DbItemTemplate Material, int Need)> missing)
        {
            var merged = new List<(DbItemTemplate Material, int Need)>();
            foreach (var (material, need) in missing)
            {
                int at = merged.FindIndex(m => m.Material.Id_nb == material.Id_nb);
                if (at < 0)
                    merged.Add((material, need));
                else
                    merged[at] = (material, merged[at].Need + need);
            }
            return merged;
        }

        /// <summary>The backpack slot of a command: 40-79, or 0-39 counted from the first backpack slot.</summary>
        public static bool TryBackpackSlot(string text, out int slot)
        {
            slot = 0;
            if (!int.TryParse(text, out int s))
                return false;
            if (s >= 0 && s < 40)
                s += (int)eInventorySlot.FirstBackpack;
            if (s < (int)eInventorySlot.FirstBackpack || s > (int)eInventorySlot.LastBackpack)
                return false;
            slot = s;
            return true;
        }

        /// <summary>1-4 distinct recipe ids, comma separated.</summary>
        public static bool TryRecipeIds(string text, out List<ushort> ids)
        {
            ids = new List<ushort>();
            if (string.IsNullOrWhiteSpace(text))
                return false;
            foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!ushort.TryParse(part.Trim(), out ushort id) || id == 0 || ids.Contains(id))
                    return false;
                ids.Add(id);
            }
            return ids.Count is >= 1 and <= 4;
        }

        /// <summary>A craft outcome to the stop reason it causes (None = keep going; a failed craft is retried).</summary>
        public static EnchantReason ReasonFor(CraftAssistOutcome outcome) => outcome switch
        {
            CraftAssistOutcome.Made or CraftAssistOutcome.Failed => EnchantReason.None,
            CraftAssistOutcome.StoppedByPlayer => EnchantReason.StoppedByPlayer,
            CraftAssistOutcome.MaterialsGone => EnchantReason.Other,
            _ => EnchantReason.Interrupted,
        };

        public static EnchantReason ReasonFor(CraftAssistReason buy) => buy switch
        {
            CraftAssistReason.None => EnchantReason.None,
            CraftAssistReason.OutOfMoney => EnchantReason.OutOfMoney,
            CraftAssistReason.BackpackFull => EnchantReason.BackpackFull,
            _ => EnchantReason.Other,
        };

        public static string ReasonText(EnchantReason reason) => reason switch
        {
            EnchantReason.Done => "done.",
            EnchantReason.OutOfMoney => "not enough money for the materials.",
            EnchantReason.BackpackFull => "your backpack is full.",
            EnchantReason.Interrupted => "interrupted (you moved, left, were attacked or were stunned).",
            EnchantReason.NotEnoughSkill => "your skill is too low.",
            EnchantReason.ItemGone => "the item is gone or can't be enchanted.",
            EnchantReason.StoppedByPlayer => "stopped by you.",
            EnchantReason.CombineFailed => "the combine failed.",
            _ => "it could not continue.",
        };

        #endregion

        #region Commands

        public static void SendInfo(GamePlayer player, int slot)
        {
            List<byte[]> payloads;
            try
            {
                payloads = InfoPayloads(player, slot);
            }
            catch (Exception ex)
            {
                log.Error("CraftEnchant165: info failed", ex);
                payloads = new List<byte[]> { BuildHeader(HeaderOf(null, slot, 0, 0)), BuildEnd() };
            }
            foreach (byte[] p in payloads)
                Hire165.Send(player, OpEnchantInfo, p);
        }

        public static void StartSpell(GamePlayer player, int slot, List<ushort> gemRecipeIds, bool allowOvercharge) =>
            Start(player, slot, false, gemRecipeIds, allowOvercharge);

        public static void StartAlchemy(GamePlayer player, int slot, ushort tinctureRecipeId) =>
            Start(player, slot, true, new List<ushort> { tinctureRecipeId }, false);

        public static void Stop(GamePlayer player, EnchantReason reason, string why)
        {
            if (!Runs.TryGetValue(player, out EnchantRun run))
            {
                SendStatus(player, new EnchantRun());
                return;
            }
            lock (run.Sync)
            {
                if (!run.Running)
                {
                    SendStatus(player, run);
                    return;
                }
                End(player, run, reason, why);
            }
        }

        public static void Status(GamePlayer player)
        {
            if (Runs.TryGetValue(player, out EnchantRun run))
                lock (run.Sync)
                    SendStatus(player, run);
            else
                SendStatus(player, new EnchantRun());
        }

        #endregion

        #region The run

        private static void Start(GamePlayer player, int slot, bool alchemy, List<ushort> ids, bool allowOvercharge)
        {
            EnchantRun run = Runs.GetOrAdd(player, _ => new EnchantRun());
            lock (run.Sync)
            {
                if (run.Running)
                {
                    Chat(player, "The enchant helper is already running. Use stop first.");
                    SendStatus(player, run);
                    return;
                }
                DbInventoryItem item = BackpackItem(player, slot);
                run.Begin(alchemy, allowOvercharge, item?.Name);
                run.Item = item;
                run.Region = player.CurrentRegionID;
                run.X = player.X;
                run.Y = player.Y;
                try
                {
                    Prepare(player, run, ids);
                }
                catch (Exception ex)
                {
                    log.Error("CraftEnchant165: start failed", ex);
                    End(player, run, EnchantReason.Other, "something went wrong.");
                }
            }
        }

        // checks, picks owned gems, buys the materials, then starts the first craft (caller holds run.Sync)
        private static void Prepare(GamePlayer player, EnchantRun run, List<ushort> ids)
        {
            if (CraftAssist165.IsRunning(player))
            {
                End(player, run, EnchantReason.Other, "stop the crafting assistant first.");
                return;
            }
            if (player.IsCrafting)
            {
                End(player, run, EnchantReason.Other, "finish or stop your current craft first.");
                return;
            }
            DbInventoryItem item = run.Item;
            if (item == null)
            {
                End(player, run, EnchantReason.ItemGone, "there is no item in that backpack slot.");
                return;
            }
            // a /craftmacro queue would re-craft after each helper craft: drop it
            player.TempProperties.RemoveProperty(CraftAction.CRAFT_QUEUE_REMAINING_PROPERTY);

            Dictionary<string, int> have = CraftAssist165Economics.BackpackCounts(player);
            var ready = new List<DbInventoryItem>();
            var crafts = new List<EnchantCraft>();
            EnchantPlan plan;
            string what;
            if (run.Alchemy)
            {
                RecipeEntry entry = FindRecipe(player, eCraftingSkill.Alchemy, ids[0], eObjectType.AlchemyTincture);
                if (entry == null)
                {
                    End(player, run, EnchantReason.Other, "that tincture recipe is not known.");
                    return;
                }
                DbItemTemplate p = entry.Recipe.Product;
                List<DbInventoryItem> owned = OwnedMatches(player, p, ready);
                var choice = new EnchantTinctureChoice
                {
                    RecipeId = entry.Id, TypeDamage = p.Type_Damage, ProcSpellID = p.ProcSpellID, SpellID = p.SpellID,
                    ProcChance = p.ProcChance, LevelRequirement = p.LevelRequirement, RequiredLevel = entry.Recipe.Level,
                    MaterialCost = MissingCost(have, entry.Recipe.Ingredients), Seconds = CraftAssist165Economics.PlayerCraftSeconds(player, entry.Recipe),
                    Owned = owned.Count, Name = p.Name, Recipe = entry.Recipe,
                };
                plan = PlanAlchemy(item, choice, player.GetCraftingSkillValue(eCraftingSkill.Alchemy));
                if (!plan.Ok)
                {
                    End(player, run, plan.Reason, plan.Why);
                    return;
                }
                if (choice.Owned > 0)
                    ready.Add(owned[0]);
                else
                    crafts.Add(CraftOf(player, entry));
                what = $"the {p.Name} on the {item.Name}";
            }
            else
            {
                var choices = new List<EnchantGemChoice>();
                var entries = new List<RecipeEntry>();
                var picked = new List<DbInventoryItem>();
                foreach (ushort id in ids)
                {
                    RecipeEntry entry = FindRecipe(player, eCraftingSkill.SpellCrafting, id, eObjectType.SpellcraftGem);
                    if (entry == null)
                    {
                        End(player, run, EnchantReason.Other, $"gem recipe {id} is not known.");
                        return;
                    }
                    DbItemTemplate p = entry.Recipe.Product;
                    List<DbInventoryItem> owned = OwnedMatches(player, p, picked);
                    if (owned.Count > 0)
                        picked.Add(owned[0]);
                    choices.Add(new EnchantGemChoice
                    {
                        RecipeId = entry.Id, BonusType = p.Bonus1Type, BonusValue = p.Bonus1, RequiredLevel = entry.Recipe.Level,
                        MaterialCost = MissingCost(have, entry.Recipe.Ingredients), Seconds = CraftAssist165Economics.PlayerCraftSeconds(player, entry.Recipe),
                        Owned = owned.Count, OwnedQuality = owned.Count > 0 ? owned[0].Quality : 0, Name = p.Name, Recipe = entry.Recipe,
                    });
                    entries.Add(entry);
                }
                plan = PlanSpell(item, choices, player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting), run.AllowOvercharge, CraftedQualityFloor);
                if (!plan.Ok)
                {
                    End(player, run, plan.Reason, plan.Why);
                    return;
                }
                foreach (int i in plan.Owned)
                    ready.Add(OwnedMatches(player, entries[i].Recipe.Product, ready)[0]);
                foreach (int i in plan.Crafted)
                    crafts.Add(CraftOf(player, entries[i]));
                string risk = plan.Over > 0
                    ? $", overcharge: {plan.SuccessChance}% success, up to {100 - plan.PreserveChance}% chance the gems explode if it fails"
                    : string.Empty;
                what = $"{ids.Count} gem{(ids.Count == 1 ? "" : "s")} on the {item.Name}: {plan.Total}/{plan.Max} imbue points (cap {plan.Cap}){risk}";
            }

            run.Ready = ready;
            run.Crafts = crafts;
            run.StepsTotal = crafts.Count + 1;
            run.UpdateSecondsLeft();
            Chat(player, $"Enchant helper: {what}. {ready.Count} from your backpack, {crafts.Count} to craft.");
            SendStatus(player, run);

            if (crafts.Count > 0)
            {
                EnchantReason bought = BuyForCrafts(player, run);
                if (bought != EnchantReason.None)
                {
                    End(player, run, bought, null);
                    return;
                }
            }
            SafeStep(player, run);
        }

        // all missing materials of all crafts at once, whole packs at merchant price (caller holds run.Sync)
        private static EnchantReason BuyForCrafts(GamePlayer player, EnchantRun run)
        {
            var ingredients = run.Crafts.SelectMany(c => c.Recipe.Ingredients).ToList();
            List<(DbItemTemplate Material, int Need)> missing = MergeMissing(CraftAssist165.MissingMaterials(player, ingredients));
            if (missing.Count == 0)
                return EnchantReason.None;
            List<CraftAssistBuyLine> lines = CraftAssist165.PlanBuy(missing);
            CraftAssistBuyLine unbuyable = lines.FirstOrDefault(l => l.Material.Price <= 0);
            if (unbuyable.Material != null)
            {
                Chat(player, $"Enchant helper: no merchant sells {unbuyable.Material.Name}; you need {unbuyable.Amount} more in your backpack.");
                return EnchantReason.Other;
            }
            long total = lines.Sum(l => l.Cost);
            long money = player.GetCurrentMoney();
            if (total > money)
            {
                Chat(player, $"Enchant helper: the materials cost {CraftAssist165.MoneyText(total)}; you have {CraftAssist165.MoneyText(money)}.");
                return EnchantReason.OutOfMoney;
            }
            int newLines = lines.Count(l => !HasOpenStack(player, l.Material));
            int empty = EmptyBackpackSlots(player);
            if (empty < newLines + run.Crafts.Count)
            {
                Chat(player, $"Enchant helper: you need {newLines + run.Crafts.Count} free backpack slots for the materials and what is made.");
                return EnchantReason.BackpackFull;
            }
            run.State = EnchantState.Buying;
            CraftAssistReason result = CraftAssist165.BuyMissing(player, missing, "Enchant helper: bought", "Enchant helper", out long paid);
            run.Spent += paid;
            return ReasonFor(result);

            static bool HasOpenStack(GamePlayer p, DbItemTemplate material)
            {
                lock (p.Inventory.Lock)
                    foreach (DbInventoryItem it in p.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                        if (it != null && it.Id_nb == material.Id_nb && it.Count < it.MaxCount)
                            return true;
                return false;
            }
        }

        private static void SafeStep(GamePlayer player, EnchantRun run)
        {
            try
            {
                Step(player, run);
            }
            catch (Exception ex)
            {
                log.Error("CraftEnchant165: step failed", ex);
                End(player, run, EnchantReason.Other, "something went wrong.");
            }
        }

        /// <summary>Starts the next craft or the combine, or ends the run. Caller holds run.Sync.</summary>
        private static void Step(GamePlayer player, EnchantRun run)
        {
            if (!run.Running)
                return;
            if (!StillHere(player, run))
            {
                End(player, run, EnchantReason.Interrupted, null);
                return;
            }
            if (player.IsCrafting)
            {
                End(player, run, EnchantReason.StoppedByPlayer, "you started crafting by hand.");
                return;
            }
            if (!InBackpack(player, run.Item))
            {
                End(player, run, EnchantReason.ItemGone, "the item left your backpack.");
                return;
            }
            if (player.IsMoving || player.IsStrafing || player.InCombat || player.IsCasting || player.IsCrowdControlled)
            {
                End(player, run, EnchantReason.Interrupted, null);
                return;
            }
            if (run.NextCraft >= run.Crafts.Count)
            {
                Combine(player, run);
                return;
            }

            EnchantCraft craft = run.Crafts[run.NextCraft];
            // the materials were bought at the start; buy again what was sold or used meanwhile (never free)
            List<(DbItemTemplate Material, int Need)> missing = CraftAssist165.MissingMaterials(player, craft.Recipe.Ingredients);
            if (missing.Count > 0)
            {
                CraftAssistReason bought = CraftAssist165.BuyMissing(player, MergeMissing(missing), "Enchant helper: bought", "Enchant helper", out long paid);
                run.Spent += paid;
                if (bought != CraftAssistReason.None)
                {
                    End(player, run, ReasonFor(bought), null);
                    return;
                }
            }
            if (!CraftAssist165.HasRoomForProduct(player, craft.Recipe))
            {
                End(player, run, EnchantReason.BackpackFull, null);
                return;
            }
            AbstractCraftingSkill skill = CraftingMgr.getSkillbyEnum(craft.Recipe.RequiredCraftingSkill);
            if (skill == null)
            {
                End(player, run, EnchantReason.Other, null);
                return;
            }

            run.State = EnchantState.Crafting;
            run.RecipeId = craft.RecipeId;
            run.UpdateSecondsLeft();
            SendStatus(player, run);
            if (!CraftAssist165.RunAssistCraft(player, skill, craft.Recipe))
                End(player, run, EnchantReason.Other, "the craft could not start (see the message above).");
        }

        /// <summary>CraftAssist165.OnCraftEnded hands an enchant craft's end here; true when this run owned it.</summary>
        public static bool OnCraftEnded(GamePlayer player, Recipe recipe, CraftAssistOutcome outcome, List<(DbInventoryItem Item, int Count)> made)
        {
            if (player == null || !Runs.TryGetValue(player, out EnchantRun run))
                return false;
            lock (run.Sync)
            {
                if (!run.Running || run.State != EnchantState.Crafting)
                    return false;
                try
                {
                    AfterCraft(player, run, outcome, made);
                }
                catch (Exception ex)
                {
                    log.Error("CraftEnchant165: after-craft failed", ex);
                    End(player, run, EnchantReason.Other, "something went wrong.");
                }
                return true;
            }
        }

        // keeps what was made, retries a failed craft, then schedules the next step (caller holds run.Sync)
        private static void AfterCraft(GamePlayer player, EnchantRun run, CraftAssistOutcome outcome, List<(DbInventoryItem Item, int Count)> made)
        {
            EnchantReason stop = ReasonFor(outcome);
            if (stop != EnchantReason.None)
            {
                End(player, run, stop, outcome == CraftAssistOutcome.MaterialsGone ? "the materials were gone when the craft finished." : null);
                return;
            }
            if (outcome == CraftAssistOutcome.Made)
            {
                EnchantCraft craft = run.Crafts[run.NextCraft];
                DbInventoryItem product = made?.Select(m => m.Item)
                    .FirstOrDefault(i => SameEnchantItem(i, craft.Recipe.Product) && !run.Ready.Any(r => ReferenceEquals(r, i)));
                product ??= OwnedMatches(player, craft.Recipe.Product, run.Ready).FirstOrDefault();
                if (product == null)
                {
                    End(player, run, EnchantReason.Other, $"the new {craft.Name} is not in your backpack.");
                    return;
                }
                run.Ready.Add(product);
                run.NextCraft++;
                run.StepIndex++;
            }
            // a failed craft lost no materials: the same craft runs again
            run.UpdateSecondsLeft();
            SendStatus(player, run);

            run.Timer?.Stop();
            run.Timer = new ECSGameTimer(player, t =>
            {
                lock (run.Sync)
                {
                    if (run.Timer == t)
                        run.Timer = null;
                    SafeStep(player, run);
                }
                return 0;
            }, CraftAssist165.NextCraftDelayMs);
        }

        // the real combine, without a trade window (caller holds run.Sync)
        private static void Combine(GamePlayer player, EnchantRun run)
        {
            run.State = EnchantState.Combining;
            run.RecipeId = 0;
            run.SecondsLeft = 0;
            SendStatus(player, run);

            foreach (DbInventoryItem r in run.Ready)
            {
                if (!InBackpack(player, r) || r.Count < 1)
                {
                    End(player, run, EnchantReason.Other, "a gem or tincture left your backpack.");
                    return;
                }
            }
            var skill = CraftingMgr.getSkillbyEnum(run.Alchemy ? eCraftingSkill.Alchemy : eCraftingSkill.SpellCrafting) as AdvancedCraftingSkill;
            if (skill == null || run.Ready.Count == 0)
            {
                End(player, run, EnchantReason.Other, null);
                return;
            }

            CombineOutcome outcome = skill.CombineWithoutWindow(player, run.Item, run.Ready);
            run.ItemDestroyed = !InBackpack(player, run.Item);   // the 1.65 Luhz rule keeps the item; reported if that ever changes
            switch (outcome)
            {
                case CombineOutcome.Applied:
                    run.StepIndex = run.StepsTotal;
                    End(player, run, EnchantReason.Done, null);
                    break;
                case CombineOutcome.Failed:
                    End(player, run, EnchantReason.CombineFailed, "the imbue failed; the gems are still in your backpack.");
                    break;
                case CombineOutcome.Exploded:
                    run.GemsLost = true;
                    End(player, run, EnchantReason.CombineFailed, run.ItemDestroyed
                        ? "the overcharge exploded: the gems and the item are lost."
                        : "the overcharge exploded: the gems are lost, the item is kept.");
                    break;
                default:
                    End(player, run, EnchantReason.ItemGone, "the item can't take that (see the message above).");
                    break;
            }
        }

        /// <summary>Ends the run: stops a craft in progress (no materials lost), tells the player, sends op 44.</summary>
        private static void End(GamePlayer player, EnchantRun run, EnchantReason reason, string why)
        {
            if (!run.End(reason))
                return;
            run.Timer?.Stop();
            run.Timer = null;
            // the run is no longer running, so the craft's cleanup report is ignored
            if (player.craftComponent?.CraftAction?.Assist == true)
                player.craftComponent.StopCraft();

            if (reason == EnchantReason.Done)
                Chat(player, $"Enchant helper: the {run.ItemName} is enchanted.");
            else
                Chat(player, $"Enchant helper stopped: {why ?? ReasonText(reason)}");
            if (run.Spent > 0)
                Chat(player, $"Enchant helper: spent {CraftAssist165.MoneyText(run.Spent)} on materials.");
            SendStatus(player, run);
        }

        private static bool StillHere(GamePlayer player, EnchantRun run)
        {
            if (player?.Client == null || player.ObjectState != GameObject.eObjectState.Active || !player.IsAlive)
                return false;
            if (player.Client.ClientState != GameClient.eClientState.Playing)
                return false;
            if (player.CurrentRegionID != run.Region)
                return false;
            long dx = player.X - run.X, dy = player.Y - run.Y;
            return dx * dx + dy * dy <= (long)CraftAssist165.MaxDrift * CraftAssist165.MaxDrift;
        }

        #endregion

        #region Game-side helpers

        /// <summary>A recipe of the player's realm by id, with its id (from the planner's per-realm cache).</summary>
        private sealed class RecipeEntry
        {
            public ushort Id;
            public Recipe Recipe;
        }

        private static RecipeEntry FindRecipe(GamePlayer player, eCraftingSkill trade, ushort id, eObjectType productType)
        {
            foreach (CraftAssist165Economics.RecipeInfo info in CraftAssist165Economics.RecipesOf(trade, player.Realm))
                if (info.Id == id && info.Recipe?.Product != null && info.Recipe.Product.Object_Type == (int)productType)
                    return new RecipeEntry { Id = id, Recipe = info.Recipe };
            return null;
        }

        private static EnchantCraft CraftOf(GamePlayer player, RecipeEntry entry)
        {
            int seconds = CraftAssist165Economics.PlayerCraftSeconds(player, entry.Recipe);
            int con = CraftAssist165Economics.ItemCon(player.GetCraftingSkillValue(entry.Recipe.RequiredCraftingSkill), entry.Recipe.Level);
            double make = Math.Max(1, CraftAssist165Economics.MakeChance(con)) / 100.0;
            return new EnchantCraft { RecipeId = entry.Id, Recipe = entry.Recipe, Name = entry.Recipe.Product.Name, ExpectedSeconds = seconds / make };
        }

        /// <summary>Backpack items that are this gem / tincture, best quality first, minus the ones already taken.</summary>
        private static List<DbInventoryItem> OwnedMatches(GamePlayer player, DbItemTemplate product, List<DbInventoryItem> taken)
        {
            var list = new List<DbInventoryItem>();
            lock (player.Inventory.Lock)
                foreach (DbInventoryItem it in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                    if (it != null && it.Count > 0 && SameEnchantItem(it, product) && !taken.Any(t => ReferenceEquals(t, it)))
                        list.Add(it);
            list.Sort((a, b) => b.Quality.CompareTo(a.Quality));
            return list;
        }

        private static DbInventoryItem BackpackItem(GamePlayer player, int slot)
        {
            if (slot < (int)eInventorySlot.FirstBackpack || slot > (int)eInventorySlot.LastBackpack)
                return null;
            return player.Inventory?.GetItem((eInventorySlot)slot);
        }

        private static bool InBackpack(GamePlayer player, DbInventoryItem item)
        {
            if (item == null)
                return false;
            int slot = item.SlotPosition;
            return slot >= (int)eInventorySlot.FirstBackpack && slot <= (int)eInventorySlot.LastBackpack &&
                ReferenceEquals(player.Inventory.GetItem((eInventorySlot)slot), item);
        }

        private static int EmptyBackpackSlots(GamePlayer player)
        {
            int n = 0;
            lock (player.Inventory.Lock)
                for (int s = (int)eInventorySlot.FirstBackpack; s <= (int)eInventorySlot.LastBackpack; s++)
                    if (player.Inventory.GetItem((eInventorySlot)s) == null)
                        n++;
            return n;
        }

        private static List<byte[]> InfoPayloads(GamePlayer player, int slot)
        {
            DbInventoryItem item = BackpackItem(player, slot);
            int spell = player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting);
            int alch = player.GetCraftingSkillValue(eCraftingSkill.Alchemy);
            EnchantHeader h = HeaderOf(item, slot, spell, alch);
            var list = new List<byte[]> { BuildHeader(h) };
            if (item != null && h.Reason != EnchantBlock.NotAllowed)
            {
                list.Add(BuildImbueRules(item, spell, CraftedQualityFloor));
                var names = new List<(ushort, string)>();
                Dictionary<string, int> have = CraftAssist165Economics.BackpackCounts(player);
                if (h.CanSpell)
                    list.AddRange(BuildGemRows(GemRows(player, item, spell, have, names)));
                if (h.CanAlchemy)
                    list.AddRange(BuildTinctureRows(TinctureRows(player, item, alch, have, names)));
                list.AddRange(BuildStrings(names));
            }
            list.Add(BuildEnd());
            return list;
        }

        // every gem recipe of the realm whose bonus is not on the item yet, one per product
        private static List<EnchantGemRow> GemRows(GamePlayer player, DbInventoryItem item, int spell, Dictionary<string, int> have, List<(ushort, string)> names)
        {
            var owned = new Dictionary<(int, int), (int Count, int Quality)>();
            lock (player.Inventory.Lock)
                foreach (DbInventoryItem it in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                    if (it != null && it.Object_Type == (int)eObjectType.SpellcraftGem && it.Count > 0)
                    {
                        var key = (it.Bonus1Type, it.Bonus1);
                        owned.TryGetValue(key, out var o);
                        owned[key] = (o.Count + it.Count, Math.Max(o.Quality, it.Quality));
                    }

            List<int> existing = ExistingBonusTypes(item);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<EnchantGemRow>();
            int floor = CraftedQualityFloor;
            foreach (CraftAssist165Economics.RecipeInfo info in CraftAssist165Economics.RecipesOf(eCraftingSkill.SpellCrafting, player.Realm))
            {
                DbItemTemplate p = info.Recipe?.Product;
                if (p == null || p.Object_Type != (int)eObjectType.SpellcraftGem || p.Bonus1Type == 0 || existing.Contains(p.Bonus1Type))
                    continue;
                if (!seen.Add(p.Id_nb))
                    continue;   // the same gem under a second recipe id
                owned.TryGetValue((p.Bonus1Type, p.Bonus1), out var o);
                List<Ingredient> ingredients = info.Recipe.Ingredients;
                bool canCraft = CanMake(spell, info.Level) && MaterialsObtainable(have, ingredients);
                rows.Add(new EnchantGemRow(info.Id, p.Bonus1Type, p.Bonus1, SpellCrafting.GemImbuePoints(p.Bonus1Type, p.Bonus1) * 10,
                    MissingCost(have, ingredients), CraftAssist165Economics.PlayerCraftSeconds(player, info.Recipe), info.Level, o.Count, canCraft,
                    o.Count > 0 ? o.Quality : floor));
                names.Add((info.Id, p.Name));
            }
            return rows;
        }

        // the tinctures of the realm that fit the item, one per product
        private static List<EnchantTinctureRow> TinctureRows(GamePlayer player, DbInventoryItem item, int alch, Dictionary<string, int> have, List<(ushort, string)> names)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<EnchantTinctureRow>();
            int charges = Alchemy.MaxChargesForQuality(item.Quality);
            var backpack = new List<DbInventoryItem>();
            lock (player.Inventory.Lock)
                foreach (DbInventoryItem it in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                    if (it != null && it.Object_Type == (int)eObjectType.AlchemyTincture && it.Count > 0)
                        backpack.Add(it);

            foreach (CraftAssist165Economics.RecipeInfo info in CraftAssist165Economics.RecipesOf(eCraftingSkill.Alchemy, player.Realm))
            {
                DbItemTemplate p = info.Recipe?.Product;
                if (p == null || p.Object_Type != (int)eObjectType.AlchemyTincture)
                    continue;
                TinctureKind kind = KindOf(p.Type_Damage, p.ProcSpellID, p.SpellID);
                if (kind == TinctureKind.None || !TinctureFits(item.Object_Type, p.Type_Damage) || !seen.Add(p.Id_nb))
                    continue;
                int owned = backpack.Where(it => SameEnchantItem(it, p)).Sum(it => it.Count);
                List<Ingredient> ingredients = info.Recipe.Ingredients;
                bool canCraft = CanMake(alch, info.Level) && MaterialsObtainable(have, ingredients);
                int spellId = kind == TinctureKind.Charges ? p.SpellID : p.ProcSpellID;
                rows.Add(new EnchantTinctureRow(info.Id, kind, kind == TinctureKind.Charges ? charges : 0, spellId, p.LevelRequirement, owned,
                    MissingCost(have, ingredients), CraftAssist165Economics.PlayerCraftSeconds(player, info.Recipe), info.Level, canCraft,
                    kind == TinctureKind.Charges ? 0 : p.ProcChance));
                string desc = SpellText(spellId, kind);
                names.Add((info.Id, string.IsNullOrEmpty(desc) ? p.Name : $"{p.Name}|{desc}"));
            }
            return rows;
        }

        /// <summary>A short spell line for a tincture ("Fire damage 45 on hit"), or null.</summary>
        private static string SpellText(int spellId, TinctureKind kind)
        {
            Spell spell;
            try
            {
                spell = SkillBase.GetSpellByID(spellId);
            }
            catch (Exception)
            {
                return null;
            }
            if (spell == null)
                return null;
            string when = kind switch
            {
                TinctureKind.WeaponProc => " on hit",
                TinctureKind.ArmourProc => " when hit",
                _ => " (charges)",
            };
            string what;
            if (spell.Damage > 0)
                what = $"{spell.DamageTypeToString()} damage {Math.Round(spell.Damage)}";
            else if (spell.Value != 0)
                what = $"{spell.Name} {Math.Round(spell.Value)}";
            else
                what = spell.Name;
            what = what?.Replace('|', '/');
            return string.IsNullOrEmpty(what) ? null : what + when;
        }

        #endregion

        #region Packets (pure: payload bytes from body index 4)

        public static void SendStatus(GamePlayer player, EnchantRun run) => Hire165.Send(player, OpEnchantStatus, BuildStatus(run));

        /// <summary>Op 43 kind 0: the item's header. Index 0 here is body [4].</summary>
        public static byte[] BuildHeader(EnchantHeader h)
        {
            byte[] name = NameBytes(h.Name);
            byte[] p = new byte[19 + name.Length + 1];
            p[0] = KindHeader;
            p[1] = (byte)Math.Clamp(h.Slot, 0, 255);
            p[2] = (byte)Math.Clamp(h.Level, 0, 255);
            p[3] = (byte)Math.Clamp(h.Quality, 0, 255);
            p[4] = (byte)Math.Clamp(h.FreeSlots, 0, 4);
            p[5] = (byte)(h.HasProc ? 1 : 0);
            p[6] = (byte)(h.CanSpell ? 1 : 0);
            p[7] = (byte)(h.CanAlchemy ? 1 : 0);
            PutU16(p, 8, h.ImbueMax);
            PutU16(p, 10, h.ImbueUsed);
            PutU16(p, 12, h.SpellSkill);
            PutU16(p, 14, h.AlchemySkill);
            PutU16(p, 16, h.Cap);
            p[18] = (byte)h.Reason;
            Array.Copy(name, 0, p, 19, name.Length);
            return p;
        }

        /// <summary>
        /// Op 43 kind 4 (extra, sent after the header): what the client needs to work out the imbue total and the risks
        /// exactly as the server will. [2..3] raw imbue points of the item's own bonuses, [4..5] the biggest of them,
        /// [6..12] overcharge success % for 0..6 points over the item's max, [13] preserve skill modifier (signed),
        /// [14] bonuses on the item, [15] quality assumed for gems still to craft, [16] charges a charge tincture gives.
        /// </summary>
        public static byte[] BuildImbueRules(DbInventoryItem item, int spellSkill, int craftedGemQuality)
        {
            byte[] p = new byte[17];
            p[0] = KindImbueRules;
            var (sum, biggest) = ExistingImbue(item);
            PutU16(p, 2, sum);
            PutU16(p, 4, biggest);
            int max = SpellCrafting.ItemMaxImbuePoints(item.Level, item.Quality);
            for (int over = 0; over <= MaxOvercharge; over++)
                p[6 + over] = (byte)Math.Clamp(SuccessChance(item.Quality, spellSkill, max, max + over), 0, 100);
            p[13] = (byte)(sbyte)SpellCrafting.PreserveSkillModifier(spellSkill);
            p[14] = (byte)ExistingBonusCount(item);
            p[15] = (byte)Math.Clamp(craftedGemQuality, 0, 255);
            p[16] = (byte)Alchemy.MaxChargesForQuality(item.Quality);
            return p;
        }

        /// <summary>Op 43 kind 1 packets, 5 rows of 20 bytes each.</summary>
        public static List<byte[]> BuildGemRows(IReadOnlyList<EnchantGemRow> rows)
        {
            var list = new List<byte[]>();
            for (int start = 0; start < rows.Count; start += RowsPerPacket)
            {
                int k = Math.Min(RowsPerPacket, rows.Count - start);
                byte[] p = new byte[2 + k * 20];
                p[0] = KindGems;
                p[1] = (byte)k;
                for (int i = 0; i < k; i++)
                {
                    EnchantGemRow r = rows[start + i];
                    int at = 2 + i * 20;
                    PutU16(p, at, r.RecipeId);
                    PutU16(p, at + 2, r.BonusType);
                    PutU16(p, at + 4, r.BonusValue);
                    PutU16(p, at + 6, r.ImbueTenths);
                    PutU32(p, at + 8, r.MaterialCost);
                    PutU16(p, at + 12, r.Seconds);
                    PutU16(p, at + 14, r.RequiredLevel);
                    p[at + 16] = (byte)Math.Clamp(r.Owned, 0, 255);
                    p[at + 17] = (byte)(r.CanCraft ? 1 : 0);
                    p[at + 18] = (byte)Math.Clamp(r.Quality, 0, 255);   // spec pad: the gem quality used for the risk
                    p[at + 19] = 0;
                }
                list.Add(p);
            }
            return list;
        }

        /// <summary>Op 43 kind 2 packets, 5 rows of 20 bytes each.</summary>
        public static List<byte[]> BuildTinctureRows(IReadOnlyList<EnchantTinctureRow> rows)
        {
            var list = new List<byte[]>();
            for (int start = 0; start < rows.Count; start += RowsPerPacket)
            {
                int k = Math.Min(RowsPerPacket, rows.Count - start);
                byte[] p = new byte[2 + k * 20];
                p[0] = KindTinctures;
                p[1] = (byte)k;
                for (int i = 0; i < k; i++)
                {
                    EnchantTinctureRow r = rows[start + i];
                    int at = 2 + i * 20;
                    PutU16(p, at, r.RecipeId);
                    p[at + 2] = (byte)r.Kind;
                    p[at + 3] = (byte)Math.Clamp(r.Charges, 0, 255);     // spec pad: charges on this item (kind 3)
                    PutU32(p, at + 4, r.SpellId);
                    p[at + 8] = (byte)Math.Clamp(r.LevelRequirement, 0, 255);
                    p[at + 9] = (byte)Math.Clamp(r.Owned, 0, 255);
                    PutU32(p, at + 10, r.MaterialCost);
                    PutU16(p, at + 14, r.Seconds);
                    PutU16(p, at + 16, r.RequiredLevel);
                    p[at + 18] = (byte)(r.CanCraft ? 1 : 0);
                    p[at + 19] = (byte)Math.Clamp(r.ProcChance, 0, 255); // spec pad: proc chance % (procs)
                }
                list.Add(p);
            }
            return list;
        }

        /// <summary>Op 43 kind 3 packets: recipe id u16 + zero-ended "name" or "name|spell description".</summary>
        public static List<byte[]> BuildStrings(IReadOnlyList<(ushort Id, string Text)> entries)
        {
            var list = new List<byte[]>();
            var buf = new List<byte>(MaxPayload);
            int k = 0;
            void Flush()
            {
                if (k == 0)
                    return;
                byte[] p = new byte[2 + buf.Count];
                p[0] = KindStrings;
                p[1] = (byte)k;
                buf.CopyTo(p, 2);
                list.Add(p);
                buf.Clear();
                k = 0;
            }
            foreach (var (id, text) in entries)
            {
                byte[] s = NameBytes(text);
                int size = 2 + s.Length + 1;
                if (2 + buf.Count + size > MaxPayload)
                    Flush();
                buf.Add((byte)id);
                buf.Add((byte)(id >> 8));
                buf.AddRange(s);
                buf.Add(0);
                k++;
            }
            Flush();
            return list;
        }

        public static byte[] BuildEnd() => new[] { KindEnd };

        /// <summary>Op 44 payload; index 0 here is body [4].</summary>
        public static byte[] BuildStatus(EnchantRun run)
        {
            byte[] name = NameBytes(run.ItemName);
            byte[] p = new byte[16 + name.Length + 1];
            p[0] = (byte)run.State;
            p[1] = (byte)run.Reason;
            p[2] = (byte)(run.ItemDestroyed ? 1 : 0);
            p[3] = (byte)Math.Clamp(run.StepIndex, 0, 255);
            p[4] = (byte)Math.Clamp(run.StepsTotal, 0, 255);
            p[5] = (byte)(run.GemsLost ? 1 : 0);      // spec pad: 1 = the overcharge exploded and the gems are lost
            PutU32(p, 6, run.Spent);
            PutU16(p, 10, run.RecipeId);
            PutU32(p, 12, (long)Math.Round(Math.Max(0, run.SecondsLeft)));
            Array.Copy(name, 0, p, 16, name.Length);
            return p;
        }

        /// <summary>UTF-8 bytes of a name, cut to MaxNameBytes, no zero inside.</summary>
        public static byte[] NameBytes(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<byte>();
            byte[] b = Encoding.UTF8.GetBytes(text.Replace('\0', ' '));
            if (b.Length <= MaxNameBytes)
                return b;
            int n = MaxNameBytes;
            while (n > 0 && (b[n] & 0xC0) == 0x80)
                n--;   // never cut a character in half
            return b[..n];
        }

        private static void PutU16(byte[] p, int at, long v)
        {
            ushort u = (ushort)Math.Clamp(v, 0, ushort.MaxValue);
            p[at] = (byte)u;
            p[at + 1] = (byte)(u >> 8);
        }

        private static void PutU32(byte[] p, int at, long v)
        {
            uint u = (uint)Math.Clamp(v, 0, uint.MaxValue);
            for (int i = 0; i < 4; i++)
                p[at + i] = (byte)(u >> (8 * i));
        }

        #endregion

        private static void Chat(GamePlayer player, string text) =>
            player?.Out?.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);

        private static readonly Logging.Logger log = Logging.LoggerManager.Create(typeof(CraftEnchant165));
    }
}
