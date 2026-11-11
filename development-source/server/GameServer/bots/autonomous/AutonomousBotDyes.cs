using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Gamebots dye their gear like players (owner 2026-11-10). On a town errand, now and then, a gamebot buys one dye
    /// from a dye merchant close by that fits a worn piece and uses it, as a player does (GamePlayerInventory.DyeItem:
    /// cloth dye on cloth and cloaks, leather dye on leather, enamel on studded/chain/plate/reinforced/scale and shields).
    /// Richer bots dye more often and pick dearer colours; poor bots seldom dye and pick cheap common ones. Never more
    /// than a tenth of the coin above the shopping reserve. A cloak or shield that carries a guild emblem (its own, or
    /// the guild's on an invited gamebot) is never dyed: the player handler wipes the emblem when it dyes.
    /// Hired companions don't shop (their player gears them).
    /// </summary>
    public static class AutonomousBotDyes
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(AutonomousBotDyes));

        // Dye item models, as GamePlayerInventory.CombineItems/DyeItem read them.
        public const int ClothDyeModel = 229, LeatherDyeModel = 494, EnamelModel = 495, WeaponLusterModel = 538;

        public const int DyeMerchantRadius = 2500;   // as the errand restocks (Supplies165)
        private const long OfferCacheMilliseconds = 600_000;

        // Wealth tiers by coin (copper; 1 gold = 10,000): under 1g, 10g, 50g, 200g, more.
        private static readonly long[] TierCoin = [10_000, 100_000, 500_000, 2_000_000];
        // The dearest colour a tier looks at: light/common (50-280), standard (1,375), 6,875, up to 60,000, anything.
        private static readonly long[] TierCeiling = [300, 1_500, 7_000, 60_000, long.MaxValue];
        // What a richer tier prefers at least, when the merchant has it (splurging on expensive colours).
        private static readonly long[] TierFloor = [0, 0, 1_375, 6_875, 33_000];
        // Chance per errand visit, once the bot's rest between dyes is over.
        private static readonly double[] TierChance = [0.05, 0.10, 0.18, 0.28, 0.40];
        // Hours of play between two dyes.
        private static readonly int[] TierCooldownHours = [24, 16, 12, 8, 6];

        public const int SpendShareDivisor = 10;   // never more than a tenth of the spendable coin

        // A later errand when nothing was dyed (no chance, no merchant, nothing fitting or affordable): 30 minutes.
        public const long RetryMilliseconds = 30 * 60_000L;

        // ---- pure rules

        /// <summary>Whether a dye of this model can colour an item of this type: the rules of GamePlayerInventory.DyeItem.</summary>
        public static bool DyeFits(int dyeModel, int objectType, int itemType) => dyeModel switch
        {
            ClothDyeModel => objectType == (int)eObjectType.Cloth ||
                             objectType == (int)eObjectType.Magical && (itemType == 26 || itemType == 8),   // cloaks, horse barding
            LeatherDyeModel => objectType == (int)eObjectType.Leather,
            EnamelModel => objectType is (int)eObjectType.Shield or (int)eObjectType.Studded or (int)eObjectType.Chain or
                               (int)eObjectType.Plate or (int)eObjectType.Reinforced or (int)eObjectType.Scale or
                               (int)eObjectType.Instrument ||
                           objectType == (int)eObjectType.Magical && itemType == 7,   // horse saddle
            WeaponLusterModel => objectType is >= 1 and <= 26,
            _ => false,
        };

        /// <summary>
        /// A colour dye a merchant sells: a dye model with a colour and a coin price, named as a dye, enamel or luster.
        /// Removers, bleach, acid washes, hull paint and the spellcraft/alchemy items that share these models are not.
        /// </summary>
        public static bool IsColourDye(string name, int model, int color, long price)
        {
            if (price <= 0 || color <= 0 || string.IsNullOrEmpty(name) ||
                model is not (ClothDyeModel or LeatherDyeModel or EnamelModel or WeaponLusterModel))
                return false;
            string n = name.ToLowerInvariant();
            if (n.Contains("remover") || n.Contains("bleach") || n.Contains("wash") || n.Contains("paint"))
                return false;
            return n.Contains("dye") || n.Contains("enamel") || n.Contains("luster");
        }

        /// <summary>0 (under 1 gold) to 4 (200 gold or more).</summary>
        public static int WealthTier(long coin)
        {
            int tier = 0;
            while (tier < TierCoin.Length && coin >= TierCoin[tier])
                tier++;
            return tier;
        }

        public static double DyeChance(int tier) => TierChance[Math.Clamp(tier, 0, TierChance.Length - 1)];

        public static long CooldownMilliseconds(int tier) =>
            TierCooldownHours[Math.Clamp(tier, 0, TierCooldownHours.Length - 1)] * 3_600_000L;

        /// <summary>The most a dye may cost: a tenth of the coin above the shopping reserve.</summary>
        public static long SpendCap(long coin, long reserve) => Math.Max(0, coin - Math.Max(0, reserve)) / SpendShareDivisor;

        /// <summary>The dearest dye this bot looks at: its tier's ceiling, never above the spend cap.</summary>
        public static long PriceCeiling(long coin, long reserve) => Math.Min(SpendCap(coin, reserve), TierCeiling[WealthTier(coin)]);

        public static long PreferredFloor(int tier) => TierFloor[Math.Clamp(tier, 0, TierFloor.Length - 1)];

        /// <summary>The rest after a dye merchant visit: the tier's hours only when a piece was dyed, else a short retry
        /// (owner 2026-11-10).</summary>
        public static long RestAfterVisit(bool dyed, int tier) => dyed ? CooldownMilliseconds(tier) : RetryMilliseconds;

        /// <summary>The slot shows a guild emblem: the cloak slot, or a shield in the left hand (by the slot looked at,
        /// not the item's stored SlotPosition; owner 2026-11-10).</summary>
        public static bool IsEmblemSlot(eInventorySlot slot, int objectType) =>
            slot == eInventorySlot.Cloak || slot == eInventorySlot.LeftHandWeapon && objectType == (int)eObjectType.Shield;

        /// <summary>
        /// A worn piece that must not be dyed for its emblem: it has its own emblem, or it is a cloak or shield on a bot
        /// whose guild shows an emblem there (AutonomousBotGuilds.Texture).
        /// </summary>
        public static bool SkipForEmblem(int itemEmblem, bool emblemSlot, int shownGuildEmblem) =>
            itemEmblem != 0 || emblemSlot && shownGuildEmblem != 0;

        /// <summary>
        /// Picks one offer: only prices within <see cref="PriceCeiling"/>; a richer tier keeps to the dear end
        /// (<see cref="PreferredFloor"/>) when there is one; otherwise a random one. Default when none is affordable.
        /// </summary>
        public static T ChooseOffer<T>(IReadOnlyList<T> offers, Func<T, long> price, long coin, long reserve, Random random)
        {
            if (offers == null || offers.Count == 0)
                return default;
            long ceiling = PriceCeiling(coin, reserve);
            List<T> affordable = offers.Where(o => price(o) > 0 && price(o) <= ceiling).ToList();
            if (affordable.Count == 0)
                return default;
            long floor = PreferredFloor(WealthTier(coin));
            List<T> preferred = affordable.Where(o => price(o) >= floor).ToList();
            List<T> pool = preferred.Count > 0 ? preferred : affordable;
            return pool[(random ?? Random.Shared).Next(pool.Count)];
        }

        // ---- live part

        public sealed record DyeOffer(GameMerchant Merchant, DbItemTemplate Template)
        {
            public long Price => Template.Price;
        }

        private sealed class OfferBox
        {
            public DyeOffer[] Offers;
            public long Until;
        }

        private static readonly ConditionalWeakTable<GameMerchant, OfferBox> OfferCache = new();
        private static readonly ConcurrentDictionary<long, long> NextDyeTick = new();

        /// <summary>Drops a bot's dye rest when it leaves the world or is retired, so archive churn doesn't grow the
        /// map (owner 2026-11-10). The next login starts a fresh random rest.</summary>
        public static void Forget(long botId) => NextDyeTick.TryRemove(botId, out _);

        private static readonly eInventorySlot[] DyeableSlots =
        [
            eInventorySlot.HeadArmor, eInventorySlot.TorsoArmor, eInventorySlot.LegsArmor, eInventorySlot.ArmsArmor,
            eInventorySlot.HandsArmor, eInventorySlot.FeetArmor, eInventorySlot.Cloak, eInventorySlot.LeftHandWeapon,
        ];

        /// <summary>
        /// On a town errand (BotBrain merchant service): maybe buy and use one dye. Cheap checks first (rest timer, chance);
        /// the nearby merchants are only looked at when the bot has decided to dye. True when a piece was dyed.
        /// </summary>
        public static bool TryDye(GameBot bot, GameMerchant at)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup ||
                HiredCompanions165.IsHired(bot) || bot.Inventory == null || bot.DatabaseID <= 0)
                return false;
            long now = GameLoop.GameLoopTime;
            long coin = AutonomousBotEconomy.GetMoney(bot.DatabaseID);
            int tier = WealthTier(coin);
            if (!NextDyeTick.TryGetValue(bot.DatabaseID, out long next))
            {
                // first errand since login/restart: start its rest at a random point, so bots don't all dye at once
                NextDyeTick[bot.DatabaseID] = now + Random.Shared.NextInt64(CooldownMilliseconds(tier) / 2 + 1);
                return false;
            }
            if (now < next)
                return false;
            // gear before colours: a bot with a gear upgrade planned keeps its coin for it
            if (AutonomousWorldBotController.CachedShoppingPlanExists(bot) == true)
                return false;
            // until a piece is dyed, only the short retry rest (owner 2026-11-10)
            NextDyeTick[bot.DatabaseID] = now + RestAfterVisit(false, tier);
            if (Random.Shared.NextDouble() >= DyeChance(tier))
                return false;
            long reserve = AutonomousWorldBotController.ShoppingReserve(bot.Level);
            if (PriceCeiling(coin, reserve) <= 0)
                return false;
            DyeOffer[] offers = NearbyOffers(bot, at);
            if (offers.Length == 0)
                return false;

            int guildEmblem = AutonomousBotGuilds.ShownGuild(bot)?.Emblem ?? 0;
            var pieces = new List<DbInventoryItem>();
            var wornColours = new HashSet<int>();
            foreach (eInventorySlot slot in DyeableSlots)
            {
                DbInventoryItem item = bot.Inventory.GetItem(slot);
                if (item == null)
                    continue;
                if (item.Color > 0)
                    wornColours.Add(item.Color);
                if (slot == eInventorySlot.LeftHandWeapon && item.Object_Type != (int)eObjectType.Shield)
                    continue;   // an off-hand weapon is no shield
                if (SkipForEmblem(item.Emblem, IsEmblemSlot(slot, item.Object_Type), guildEmblem) ||
                    DOL.GS.Quests.Hibernia.SluaghbinderEpicArmor.IsSetItem(item))
                    continue;
                pieces.Add(item);
            }
            if (pieces.Count == 0)
                return false;
            // an undyed piece first, then any
            DbInventoryItem target = null;
            DyeOffer choice = null;
            foreach (DbInventoryItem piece in pieces.OrderBy(p => p.Color > 0 ? 1 : 0).ThenBy(_ => Random.Shared.Next()))
            {
                DyeOffer[] fits = offers.Where(o => DyeFits(o.Template.Model, piece.Object_Type, piece.Item_Type) &&
                                                    o.Template.Color != piece.Color).ToArray();
                if (fits.Length == 0)
                    continue;
                // half the time a player matches a colour already worn, when it is in reach
                DyeOffer[] matching = fits.Where(o => wornColours.Contains(o.Template.Color)).ToArray();
                DyeOffer pick = matching.Length > 0 && Random.Shared.Next(2) == 0
                    ? ChooseOffer(matching, o => o.Price, coin, reserve, Random.Shared)
                    : null;
                pick ??= ChooseOffer(fits, o => o.Price, coin, reserve, Random.Shared);
                if (pick == null)
                    continue;
                target = piece;
                choice = pick;
                break;
            }
            if (target == null || !AutonomousBotEconomy.TrySpend(bot.DatabaseID, choice.Price))
                return false;
            NextDyeTick[bot.DatabaseID] = now + RestAfterVisit(true, tier);   // dyed: the tier's hours of rest

            // as the player handler: the dye's colour, no emblem (none was there), the dye used up
            int oldColour = target.Color;
            target.Color = choice.Template.Color;
            target.Emblem = 0;
            target.Dirty = true;
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            if (bot.ObjectState == GameObject.eObjectState.Active)
                bot.BroadcastLivingEquipmentUpdate();   // nearby clients see the new colour
            Log.Info($"BOT_DYED_165 bot={bot.Name} id={bot.DatabaseID} level={bot.Level} tier={tier} coin={coin} " +
                     $"item=\"{target.Name}\" colour={oldColour}->{target.Color} dye=\"{choice.Template.Name}\" " +
                     $"copper={choice.Price} from=\"{choice.Merchant.Name}\"");
            return true;
        }

        /// <summary>Colour dyes on sale at this merchant and coin merchants of the bot's realm close by.</summary>
        private static DyeOffer[] NearbyOffers(GameBot bot, GameMerchant at)
        {
            var offers = new List<DyeOffer>();
            var seen = new HashSet<GameMerchant>();
            IEnumerable<GameMerchant> merchants = at == null
                ? bot.GetNPCsInRadius(DyeMerchantRadius).OfType<GameMerchant>()
                : new[] { at }.Concat(bot.GetNPCsInRadius(DyeMerchantRadius).OfType<GameMerchant>());
            foreach (GameMerchant m in merchants)
            {
                if (m == null || !seen.Add(m) || m.ObjectState != GameObject.eObjectState.Active ||
                    m.Realm != eRealm.None && m.Realm != bot.Realm || !AutonomousBotEconomy.SellsForCoin(m))
                    continue;
                offers.AddRange(OffersOf(m));
            }
            return offers.ToArray();
        }

        private static DyeOffer[] OffersOf(GameMerchant merchant)
        {
            long now = GameLoop.GameLoopTime;
            if (OfferCache.TryGetValue(merchant, out OfferBox box) && now < box.Until)
                return box.Offers;
            DyeOffer[] offers = merchant.TradeItems == null
                ? []
                : merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>()
                    .Where(t => IsColourDye(t.Name, t.Model, t.Color, t.Price))
                    .Select(t => new DyeOffer(merchant, t)).ToArray();
            OfferCache.AddOrUpdate(merchant, new OfferBox { Offers = offers, Until = now + OfferCacheMilliseconds });
            return offers;
        }
    }
}
