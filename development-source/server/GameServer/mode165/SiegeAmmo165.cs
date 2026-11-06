using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>The 1.64 client's siege ammunition (tdl.irf Siegecraft 4160-4164 and the realm copies).</summary>
    public enum SiegeAmmoKind { None, CatapultStone, BallistaBolt, Bullet, Grapeshot, BallOfIce }

    /// <summary>What a bot operator's engine needs before it can be aimed and fired.</summary>
    public enum SiegeFeed { Ready, Loading, Empty }

    /// <summary>
    /// True 1.65 Mode siege ammunition (owner 2026-11-06: "In 1.65 siege engines needed ammunition ... No ammo, no shot").
    /// Period rules (Freddyshouse RvR boards 2002-2004, Prima, the 1.64 client):
    /// - catapults, onagers and trebuchets throw catapult stone, bullet, grapeshot or ball of ice; ballistas, scorpions and
    ///   palintones shoot ballista bolts; rams need none ("ram doesn't need ammo", FH 125966 / 153136). The cauldron is
    ///   not a 1.6x engine and is left alone.
    /// - ammunition is put into the engine from the operator's backpack, one piece at a time ("One at a time only",
    ///   FH 125966; "drag it from your inventory onto the catapult", FH 166818). Since 1.54f an engine holds several
    ///   pieces and the crew selects which one to load (FH 101036); about eight (FH 157111).
    /// - LOAD takes about five seconds ("begin loading ... loaded and ready", FH 105630) and each shot spends the piece.
    /// - ballista bolts and catapult stones were sold by the siege merchants (FH 157111, Dec 2003) and portal-keep
    ///   merchants (FH 86463, 101036); bullet, grapeshot and ball of ice were crafter-only (FH 132447).
    /// Normal mode is untouched: every rule here answers "no ammunition needed" unless Classic165Mode is on.
    /// </summary>
    public static class SiegeAmmo165
    {
        /// <summary>Pieces an engine holds, the loaded one included (FH 157111: "about 8").</summary>
        public const int EngineCapacity = 8;
        /// <summary>Loading a piece (FH 105630: 21:53:13 begin loading, 21:53:18 loaded).</summary>
        public const int LoadMilliseconds = 5000;
        /// <summary>A bot crew restocks to one period stack (1.60b: "Ballista and Catapult ammo now stack up to 20").</summary>
        public const int RestockTarget = 20;
        /// <summary>At most one restock attempt per crew in this time: a supply run, and it keeps the scans rare.</summary>
        public const long RestockRetryMilliseconds = 120_000;
        /// <summary>Skill a siegecrafter needs for catapult stones and ballista bolts (tdl.irf CraftingLevel 10).</summary>
        public const int BasicRecipeSkill = 10;

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(SiegeAmmo165));

        // ---- identity and fit

        /// <summary>The ammunition an item is, from its id (the realm copies end in 2 or 3).</summary>
        public static SiegeAmmoKind KindOf(string idNb)
        {
            if (string.IsNullOrEmpty(idNb)) return SiegeAmmoKind.None;
            string id = idNb.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').ToLowerInvariant();
            return id switch
            {
                "catapult_stone" => SiegeAmmoKind.CatapultStone,
                "ballista_bolt" => SiegeAmmoKind.BallistaBolt,
                "bullet" => SiegeAmmoKind.Bullet,
                "grapeshot" => SiegeAmmoKind.Grapeshot,
                "ball_of_ice" => SiegeAmmoKind.BallOfIce,
                _ => SiegeAmmoKind.None,
            };
        }

        /// <summary>The period rule, mode-independent: every engine but the ram fires ammunition.</summary>
        public static bool NeedsAmmo(BotSiegeKind kind) => kind != BotSiegeKind.Ram;

        /// <summary>True 1.65 Mode and an engine of this kind fires ammunition.</summary>
        public static bool Required(BotSiegeKind kind) => Classic165Mode.IsOn && NeedsAmmo(kind);

        /// <summary>Engines that fire ammunition (catapult and its trebuchet, ballista); not rams or cauldrons.</summary>
        public static bool FiresAmmo(GameSiegeWeapon weapon) => weapon is GameSiegeCatapult or GameSiegeBallista;

        /// <summary>True 1.65 Mode and this engine fires ammunition.</summary>
        public static bool UsesAmmo(GameSiegeWeapon weapon) => Classic165Mode.IsOn && FiresAmmo(weapon);

        public static bool Fits(BotSiegeKind engine, SiegeAmmoKind ammo) => engine switch
        {
            BotSiegeKind.Ballista => ammo == SiegeAmmoKind.BallistaBolt,
            BotSiegeKind.Catapult or BotSiegeKind.Trebuchet => ammo is SiegeAmmoKind.CatapultStone or SiegeAmmoKind.Bullet or
                SiegeAmmoKind.Grapeshot or SiegeAmmoKind.BallOfIce,
            _ => false,
        };

        public static bool Fits(GameSiegeWeapon weapon, SiegeAmmoKind ammo) => FiresAmmo(weapon) && Fits(BotSiegeRuntime.Kind(weapon), ammo);

        /// <summary>The merchant-sold ammunition of an engine kind (bots use only these).</summary>
        public static SiegeAmmoKind BasicAmmo(BotSiegeKind kind) => kind switch
        {
            BotSiegeKind.Ballista => SiegeAmmoKind.BallistaBolt,
            BotSiegeKind.Catapult or BotSiegeKind.Trebuchet => SiegeAmmoKind.CatapultStone,
            _ => SiegeAmmoKind.None,
        };

        /// <summary>The realm's item id (Albion plain, Midgard 2, Hibernia 3, as the 1.64 recipes).</summary>
        public static string AmmoId(eRealm realm, SiegeAmmoKind kind)
        {
            string id = kind switch
            {
                SiegeAmmoKind.CatapultStone => "catapult_stone",
                SiegeAmmoKind.BallistaBolt => "ballista_bolt",
                SiegeAmmoKind.Bullet => "bullet",
                SiegeAmmoKind.Grapeshot => "grapeshot",
                SiegeAmmoKind.BallOfIce => "ball_of_ice",
                _ => null,
            };
            return id == null ? null : id + (realm == eRealm.Midgard ? "2" : realm == eRealm.Hibernia ? "3" : "");
        }

        // ---- what a shot does (catapult line; the stone is the engine's own numbers)

        /// <summary>Damage of a catapult-line shot. Late-2003 guide (FH 157111) per hit: stone 150-200, bullet 300-350,
        /// grapeshot 100-150; the 1.55 notes (FH 104722): bullet "higher damage, low area", grapeshot "lower damage, high
        /// area", ball of ice like the stone but cold. The stone (and no ammunition, normal mode) keeps the engine's damage.</summary>
        public static int ShotDamage(int damage, SiegeAmmoKind ammo) => ammo switch
        {
            SiegeAmmoKind.Bullet => damage * 185 / 100,
            SiegeAmmoKind.Grapeshot => damage * 70 / 100,
            _ => damage,
        };

        /// <summary>Splash radius: grapeshot about 450 (FH 105630 measured 450-500), bullet half the stone's.</summary>
        public static int ShotRadius(int radius, SiegeAmmoKind ammo) => ammo switch
        {
            SiegeAmmoKind.Bullet => radius / 2,
            SiegeAmmoKind.Grapeshot => Math.Max(radius, 450),
            _ => radius,
        };

        public static eDamageType ShotDamageType(eDamageType type, SiegeAmmoKind ammo) =>
            ammo == SiegeAmmoKind.BallOfIce ? eDamageType.Cold : type;

        // ---- bot crews

        /// <summary>How many pieces a crew buys or makes: up to the target, as many as it can pay for.</summary>
        public static int RestockCount(int held, long money, long unitCost, int target = RestockTarget) =>
            unitCost <= 0 ? 0 : (int)Math.Max(0, Math.Min(target - held, Math.Max(0, money) / unitCost));

        /// <summary>Ammunition in a living's backpack that fits this engine kind.</summary>
        public static int Carried(GameLiving living, BotSiegeKind kind)
        {
            if (living?.Inventory == null) return 0;
            int count = 0;
            foreach (DbInventoryItem item in living.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                if (item != null && item.Count > 0 && Fits(kind, KindOf(item.Id_nb))) count += item.Count;
            return count;
        }

        /// <summary>
        /// One step of a bot operator's ammunition work, called when its engine's timer is idle: a loaded engine is Ready;
        /// otherwise the bot puts what it carries into the engine (up to its capacity) and loads a piece (Loading, five
        /// seconds); with nothing in the engine or the pack it is Empty. Only engines that fire ammunition in True 1.65 Mode.
        /// </summary>
        public static SiegeFeed Feed(GameBot bot, GameSiegeWeapon weapon)
        {
            if (!UsesAmmo(weapon) || weapon.LoadedAmmo != null) return SiegeFeed.Ready;
            if (weapon.SiegeWeaponTimer?.IsAlive == true) return SiegeFeed.Loading;
            if (weapon.StoredAmmo == 0) PutCarried(bot, weapon);
            if (weapon.StoredAmmo == 0) return SiegeFeed.Empty;
            weapon.Load(0);
            return SiegeFeed.Loading;
        }

        /// <summary>A bot puts the fitting ammunition it carries into its engine (all it holds, up to the capacity).</summary>
        public static int PutCarried(GameBot bot, GameSiegeWeapon weapon)
        {
            if (bot?.Inventory == null || weapon == null) return 0;
            int moved = 0;
            lock (bot.Inventory.Lock)
            {
                foreach (DbInventoryItem item in bot.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).ToArray())
                {
                    if (item == null || item.Count <= 0 || !Fits(weapon, KindOf(item.Id_nb))) continue;
                    int room = weapon.AmmoRoom;
                    if (room <= 0) break;
                    int n = Math.Min(room, item.Count);
                    DbItemTemplate template = item.Template;
                    if (!bot.Inventory.RemoveCountFromStack(item, n)) continue;
                    moved += weapon.StoreAmmo(template, n);
                }
            }
            if (moved > 0)
            {
                AutonomousBotEconomy.MarkInventoryChanged(bot);
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot, true);
            }
            return moved;
        }

        /// <summary>Who pays for a crew's ammunition, and how a failed delivery is refunded.</summary>
        public sealed record Purse(Func<long, bool> Spend, Action<long> Refund, Func<long> Money);

        public static Purse BotPurse(GameBot bot) => new(
            copper => AutonomousBotEconomy.TrySpend(bot.DatabaseID, copper),
            copper => AutonomousBotEconomy.AddMoney(bot.DatabaseID, copper),
            () => AutonomousBotEconomy.GetMoney(bot.DatabaseID));

        /// <summary>A hired companion pays from its own coin; when it has too little, its player pays (and is told).</summary>
        public static Purse CompanionPurse(GameBot bot)
        {
            GamePlayer owner = bot?.Owner as GamePlayer;
            return new(
                copper =>
                {
                    if (AutonomousBotEconomy.TrySpend(bot.DatabaseID, copper)) return true;
                    if (owner == null || !owner.RemoveMoney(copper)) return false;
                    owner.Out.SendMessage($"{bot.Name} buys siege ammunition with your coin ({Money.GetString(copper)}).",
                        PacketHandler.eChatType.CT_Merchant, PacketHandler.eChatLoc.CL_SystemWindow);
                    return true;
                },
                copper => AutonomousBotEconomy.AddMoney(bot.DatabaseID, copper),
                () => AutonomousBotEconomy.GetMoney(bot.DatabaseID) + (owner?.GetCurrentMoney() ?? 0));
        }

        private static readonly ConcurrentDictionary<long, long> NextRestock = new();

        /// <summary>
        /// Whether a crew can work an engine of this kind: True 1.65 Mode artillery needs ammunition in the engine or the
        /// crew's pack. With none, the crew restocks (at most once per RestockRetryMilliseconds); false means it has none
        /// and should leave the engine for door work. Rams, and everything in normal mode, are always ready.
        /// </summary>
        public static bool Ready(GameBot bot, GameSiegeWeapon engine, BotSiegeKind kind, long now, bool companion = false)
        {
            if (!Required(kind)) return true;
            if (engine != null && BotSiegeRuntime.Kind(engine) == kind && engine.HasAmmo) return true;
            if (Carried(bot, kind) > 0) return true;
            if (bot == null || now < NextRestock.GetValueOrDefault(bot.DatabaseID)) return false;
            NextRestock[bot.DatabaseID] = now + RestockRetryMilliseconds;
            return Restock(bot, kind, companion ? CompanionPurse(bot) : BotPurse(bot)) > 0;
        }

        /// <summary>
        /// Muster-time restock of the engine kind's merchant ammunition to RestockTarget pieces. Sources, in order:
        /// 1. the crew is a siegecrafter (skill 10+): it makes them, paying the 1.64 recipe's materials;
        /// 2. a siegecrafter gamebot of the realm sells from its stock (CrafterBots165.SellAmmo);
        /// 3. the realm's siege / portal-keep merchant price (period: those merchants sold stones and bolts).
        /// Returns the pieces added to the crew's backpack.
        /// </summary>
        public static int Restock(GameBot bot, BotSiegeKind kind, Purse purse)
        {
            if (!Required(kind) || bot?.Inventory == null || purse == null) return 0;
            string id = AmmoId(bot.Realm, BasicAmmo(kind));
            DbItemTemplate template = Template(id);
            if (template == null)
            {
                Log.Warn($"SIEGE_AMMO_MISSING item={id}: the True 1.65 ammunition templates are not in this database");
                return 0;
            }
            int held = bot.Inventory.CountItemTemplate(id, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            int want = RestockTarget - held;
            if (want <= 0) return 0;
            string source = "merchant";
            int got = Buy(bot, template, want, purse);
            if (got > 0)
                Log.Info($"SIEGE_AMMO_RESTOCK bot={bot.Name} id={bot.DatabaseID} realm={bot.Realm} item={id} count={got} source={source}");
            return got;
        }

        /// <summary>Buys from the realm's merchant price list (the template price, as GameMerchant charges).</summary>
        private static int Buy(GameBot bot, DbItemTemplate template, int want, Purse purse)
        {
            int count = RestockCount(0, purse.Money(), template.Price, want);
            if (count <= 0) return 0;
            long cost = checked(template.Price * count);
            if (!purse.Spend(cost)) return 0;
            if (!Give(bot, template, count)) { purse.Refund(cost); return 0; }
            return count;
        }

        /// <summary>Adds pieces to a bot's backpack as stacks (all or nothing).</summary>
        internal static bool Give(GameBot bot, DbItemTemplate template, int count)
        {
            if (bot?.Inventory == null || template == null || count <= 0) return false;
            DbInventoryItem item = GameInventoryItem.Create(template);
            if (item == null || !bot.Inventory.AddTemplate(item, count, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                return false;
            AutonomousBotEconomy.MarkInventoryChanged(bot);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            return true;
        }

        private static readonly ConcurrentDictionary<string, DbItemTemplate> Templates = new();

        public static DbItemTemplate Template(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Templates.TryGetValue(id, out DbItemTemplate cached)) return cached;
            DbItemTemplate template = GameServer.Database.FindObjectByKey<DbItemTemplate>(id);
            if (template != null) Templates[id] = template;
            return template;
        }
    }
}
