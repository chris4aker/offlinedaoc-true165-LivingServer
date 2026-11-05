using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;

namespace DOL.GS;

/// <summary>A player's listing time (owner 2026-11-05, reset-day S38): 12, 24 or 48 hours, chosen when it is priced.</summary>
[DataTable(TableName = "offline_true165_listing_hours")]
public sealed class OfflineTrue165ListingHours : DataObject
{
    [PrimaryKey] public string InventoryId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Hours { get; set; }
}

public static class RealmExchangeExpiry
{
    public static readonly TimeSpan BotListingLifetime = TimeSpan.FromHours(24);
    public static readonly int[] PlayerHours = [12, 24, 48];
    public const int DefaultPlayerHours = 48;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> HoursCache = new();
    private static Timer _timer;
    private static int _running;

    private static bool IsBotListing(DbInventoryItem item) => AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out _);

    /// <summary>A player listing's time: the hours chosen for it, 48 if none was chosen.</summary>
    public static int PlayerListingHours(DbInventoryItem item)
    {
        if (item?.ObjectId == null) return DefaultPlayerHours;
        if (HoursCache.TryGetValue(item.ObjectId, out int hours)) return hours;
        if (GameServer.Database == null) return DefaultPlayerHours;
        hours = GameServer.Database.FindObjectByKey<OfflineTrue165ListingHours>(item.ObjectId)?.Hours ?? DefaultPlayerHours;
        HoursCache[item.ObjectId] = hours;
        return hours;
    }

    public static void SetPlayerListingHours(DbInventoryItem item, int hours)
    {
        if (item?.ObjectId == null || !PlayerHours.Contains(hours)) return;
        var record = GameServer.Database.FindObjectByKey<OfflineTrue165ListingHours>(item.ObjectId);
        if (record == null)
            GameServer.Database.AddObject(new OfflineTrue165ListingHours { InventoryId = item.ObjectId, Hours = hours });
        else
        {
            record.Hours = hours;
            GameServer.Database.SaveObject(record);
        }
        HoursCache[item.ObjectId] = hours;
    }

    /// <summary>
    /// When a listing ends: a bot's 24 h after it was listed; a player's 12, 24 or 48 h after it was priced (owner
    /// 2026-11-05: player listings never expired). A listing priced before then, or stored at no price, has none.
    /// </summary>
    public static DateTime? ExpiresUtc(DbInventoryItem item)
    {
        if (item == null || !RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot) ||
            !DateTime.TryParse(item.RealmExchangeListedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime listed))
            return null;
        if (IsBotListing(item))
            return listed.ToUniversalTime().Add(BotListingLifetime);
        return item.SellPrice > 0 ? listed.ToUniversalTime().AddHours(PlayerListingHours(item)) : null;
    }

    public static bool IsExpired(DbInventoryItem item, DateTime utcNow) => ExpiresUtc(item) is DateTime expiry && expiry <= utcNow;

    /// <summary>Minutes left on a listing for the new client: 0xFFFF none (no time limit), 0 not offered.</summary>
    public static ushort MinutesLeft(DbInventoryItem item, DateTime utcNow)
    {
        if (item == null || item.SellPrice <= 0) return 0;
        if (ExpiresUtc(item) is not DateTime expiry) return 0xFFFF;
        return (ushort)Math.Clamp((int)Math.Ceiling((expiry - utcNow).TotalMinutes), 1, 0xFFFE);
    }

    public static string Describe(DbInventoryItem item, DateTime utcNow)
    {
        bool bot = IsBotListing(item);
        if (ExpiresUtc(item) is not DateTime expiry)
            return bot ? "Bot listing." : item?.SellPrice > 0 ? "Player listing: no expiration." : "Stored, not offered for sale.";
        TimeSpan remaining = expiry - utcNow;
        string who = bot ? "Bot" : "Player";
        return remaining <= TimeSpan.Zero ? $"{who} listing expired; awaiting removal." :
            $"{who} listing expires in {(int)remaining.TotalHours}h {remaining.Minutes}m ({expiry:yyyy-MM-dd HH:mm} UTC).";
    }

    internal static void Start()
    {
        // Old listings predate the timestamp column: give those bots one full
        // bot-listing lifetime from migration, never expire an item on an unknown age.
        foreach (DbInventoryItem item in MarketCache.SearchItems(new ItemQuery()))
        {
            if (!RealmExchangeBroker.IsExchangeOwnerLot(item.OwnerLot) ||
                !AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out _) || ExpiresUtc(item) != null) continue;
            item.RealmExchangeListedUtc = DateTime.UtcNow.ToString("O");
            GameServer.Database.SaveObject(item);
        }
        _timer ??= new Timer(_ => Sweep(), null, 60_000, 60_000);
    }

    private static void Sweep()
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) return;
        try { ExpireDue(DateTime.UtcNow); }
        catch (Exception error) { Logging.LoggerManager.Create(typeof(RealmExchangeExpiry)).Error("Realm Exchange expiry sweep failed; retained remaining items.", error); }
        finally { Volatile.Write(ref _running, 0); }
    }

    public static int ExpireDue(DateTime utcNow)
    {
        int removed = 0;
        // Once a minute, bounded writes; nothing runs in individual AI turns.
        foreach (DbInventoryItem item in MarketCache.SearchItems(new ItemQuery()).Where(item => IsExpired(item, utcNow)).Take(200))
        {
            lock (RealmExchangeBroker.TransactionLock)
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                if (!IsExpired(item, utcNow)) continue;
                if (!AutonomousBotEconomy.TryParseOwnerId(item.OwnerID, out long id))
                {
                    // a player's listing ran out: it stays in their listing chest, no longer offered (they can price it again)
                    MarketCache.UpdateItem(item, static (listed, value) => listed.SellPrice = value, 0);
                    item.RealmExchangeListedUtc = string.Empty;
                    item.Dirty = true;
                    GameServer.Database.SaveObject(item);
                    if (ClientService.Instance.GetPlayers().FirstOrDefault(p => p.InternalID == item.OwnerID) is GamePlayer seller)
                        seller.Out.SendMessage($"Your Realm Exchange listing of {item.Name} ran out unsold; it waits in your listing chest.", PacketHandler.eChatType.CT_Merchant, PacketHandler.eChatLoc.CL_SystemWindow);
                    removed++;
                    continue;
                }
                // True 1.65 Mode: relisted at 90%, then 80%, then back to the bot (Exchange165); normal mode deletes it
                if (Exchange165.HandleExpired(item, id))
                {
                    AutonomousBotEconomy.MarkEconomyChanged(id);
                    removed++;
                    continue;
                }
                if (!GameServer.Database.DeleteObject(item)) continue;
                MarketCache.RemoveItem(item);
                AutonomousBotEconomy.MarkEconomyChanged(id);
                removed++;
            }
        }
        return removed;
    }
}
