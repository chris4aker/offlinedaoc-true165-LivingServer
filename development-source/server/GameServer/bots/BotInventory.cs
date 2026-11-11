using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DOL.Database;

namespace DOL.GS
{
    public class BotInventory : GameLivingInventory
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(BotInventory));
        private readonly string _persistenceOwnerId;
        private readonly HashSet<DbInventoryItem> _itemsBeingSaved = new(ReferenceEqualityComparer.Instance);

        public BotInventory()
        {
        }

        public BotInventory(string persistenceOwnerId)
        {
            _persistenceOwnerId = persistenceOwnerId;
        }

        public bool IsPersistent => !string.IsNullOrWhiteSpace(_persistenceOwnerId);

        /// <summary>
        /// A copy of the items taken under the inventory lock (owner 2026-11-10). Loot from a group member, a purchase or a
        /// save can change a bot's inventory on another thread while its AI scans it; the live collection then threw
        /// "Collection was modified" inside the AI turn (TryEquipRealInstrument, CoatFromVial) and NpcService removed the
        /// bot from the world. One copy here covers every caller.
        /// </summary>
        public override ICollection<DbInventoryItem> AllItems
        {
            get
            {
                lock (Lock)
                    return m_items.Values.ToArray();
            }
        }

        public override Task<IList> StartLoadFromDatabaseTask(string inventoryId)
        {
            string ownerId = string.IsNullOrWhiteSpace(inventoryId) ? _persistenceOwnerId : inventoryId;
            if (string.IsNullOrWhiteSpace(ownerId))
                return Task.FromResult<IList>(Array.Empty<DbInventoryItem>());
            return DOLDB<DbInventoryItem>.SelectObjectsAsync(PersonalItems(ownerId)).ContinueWith(task => task.Result as IList);
        }

        /// <summary>
        /// The bot's own items, read on the calling thread (a background thread: never the game loop). Handle census of
        /// 2026-10-21: the server's Event handles grew by about 1,500 an hour while every other kind stayed flat; the bot
        /// login poller blocked on the asynchronous load for every login, and a blocking wait on an unfinished task makes a
        /// kernel event that lives until the task is finalized.
        /// </summary>
        public IList SelectPersonalItems(string ownerId) =>
            string.IsNullOrWhiteSpace(ownerId) ? Array.Empty<DbInventoryItem>() : DOLDB<DbInventoryItem>.SelectObjects(PersonalItems(ownerId)) as IList;

        private static WhereClause PersonalItems(string ownerId) =>
            DB.Column("OwnerID").IsEqualTo(ownerId).And(DB.Column("SlotPosition").IsLessOrEqualTo((int)eInventorySlot.LastVault)
                .Or(DB.Column("SlotPosition").IsGreaterOrEqualTo(500).And(DB.Column("SlotPosition").IsLessThan(600))));

        public override bool LoadFromDatabase(string inventoryId)
        {
            IList items = GameLoopAsyncHelper.GetResult(StartLoadFromDatabaseTask(inventoryId));
            return LoadInventory(inventoryId, items);
        }

        public override bool LoadInventory(string inventoryId, IList items)
        {
            lock (Lock)
            {
                m_items.Clear();
                foreach (DbInventoryItem item in items ?? Array.Empty<DbInventoryItem>())
                {
                    eInventorySlot slot = (eInventorySlot)item.SlotPosition;
                    if (GetValidInventorySlot(slot) == eInventorySlot.Invalid || m_items.ContainsKey(slot))
                        continue;
                    m_items.Add(slot, item);
                }
            }
            return true;
        }

        public override bool SaveIntoDatabase(string inventoryId)
        {
            string ownerId = string.IsNullOrWhiteSpace(inventoryId) ? _persistenceOwnerId : inventoryId;
            if (string.IsNullOrWhiteSpace(ownerId))
                return false;

            return SaveManyIntoDatabase([(this, ownerId)]);
        }

        /// <summary>
        /// Persists several real bot inventories with one bulk delete/save/add
        /// sequence. The old path opened up to three database transactions per
        /// bot while holding the global writer lock. This keeps every physical
        /// item and slot, but dramatically shortens that lock window.
        /// </summary>
        public static bool SaveManyIntoDatabase(IEnumerable<(BotInventory Inventory, string OwnerId)> inventories)
        {
            // Listing, purchases, logout and the rolling writer must not race
            // each other's persistence flags or ownership changes.
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
                return SaveManyIntoDatabaseLocked(inventories);
        }

        private static bool SaveManyIntoDatabaseLocked(IEnumerable<(BotInventory Inventory, string OwnerId)> inventories)
        {
            var captures = new List<(BotInventory Inventory, List<DbInventoryItem> Deleted)>();
            List<DbInventoryItem> add = [];
            List<DbInventoryItem> update = [];
            List<DbInventoryItem> remove = [];
            var written = new List<(BotInventory Inventory, DbInventoryItem Live, DbInventoryItem Snapshot)>();
            var deletedCopies = new List<(DbInventoryItem Live, DbInventoryItem Snapshot)>();
            // owner 2026-11-10: row id -> owner for every row this batch adds or updates. One item held by two bots
            // was inserted twice and failed the whole batch (UNIQUE constraint failed: Inventory.Inventory_ID).
            var writtenIds = new Dictionary<string, string>();

            try
            {
            foreach ((BotInventory inventory, string ownerId) in inventories)
            {
                if (inventory == null || string.IsNullOrWhiteSpace(ownerId))
                    continue;

                List<DbInventoryItem> deleted;
                lock (inventory.Lock)
                {
                    HashSet<string> heldIds = inventory.DropDuplicateSlotsLocked(ownerId);
                    deleted = inventory.TakeDeletionsLocked(ownerId, heldIds);
                    foreach (DbInventoryItem item in deleted)
                    {
                        DbInventoryItem snapshot = item.CreatePersistenceSnapshot();
                        remove.Add(snapshot);
                        deletedCopies.Add((item, snapshot));
                    }
                    foreach ((eInventorySlot slot, DbInventoryItem item) in inventory.m_items)
                    {
                        if (item == null || item is GameInventoryRelic)
                            continue;
                        bool emptyStack = item.IsStackable && item.Count == 0;
                        if (!emptyStack && item.ObjectId != null && writtenIds.TryGetValue(item.ObjectId, out string firstOwner))
                        {
                            // owner 2026-11-10: the first bot keeps the row this batch; never write one row twice
                            Log.Warn($"BOT_INVENTORY_SHARED_ITEM id={item.ObjectId} item=\"{item.Name}\" owner={firstOwner} alsoHeldBy={ownerId} slot={(int)slot}: saved once, for the first owner");
                            continue;
                        }
                        item.OwnerID = ownerId;
                        item.SlotPosition = (int)slot;
                        // The actor may move/sell this item as soon as Lock is
                        // released. Only detached scalar state reaches SQLite.
                        DbInventoryItem snapshot = item.CreatePersistenceSnapshot();
                        inventory._itemsBeingSaved.Add(item);
                        written.Add((inventory, item, snapshot));
                        if (!emptyStack && item.ObjectId != null)
                            writtenIds[item.ObjectId] = ownerId;
                        if (emptyStack)
                        {
                            remove.Add(snapshot);
                            deletedCopies.Add((item, snapshot));
                        }
                        else if (item.IsPersisted)
                            update.Add(snapshot);
                        else
                            add.Add(snapshot);
                    }
                }
                captures.Add((inventory, deleted));
            }

            // owner 2026-11-10: a tombstone whose row another bot in this batch still holds keeps its row.
            for (int i = remove.Count - 1; i >= 0; i--)
            {
                DbInventoryItem gone = remove[i];
                if (gone.ObjectId == null || !writtenIds.TryGetValue(gone.ObjectId, out string holder))
                    continue;
                remove.RemoveAt(i);
                deletedCopies.RemoveAll(entry => ReferenceEquals(entry.Snapshot, gone));
                Log.Warn($"BOT_INVENTORY_DELETE_SKIPPED id={gone.ObjectId} item=\"{gone.Name}\" holder={holder}: still held, row kept");
            }

            // Persist definitions before their new inventory references. The ORM
            // otherwise inserts the parent first, then its relations separately;
            // a failed relation write leaves a persisted parent that may be clean
            // on the next flush. Do not manufacture definitions for legacy orphans.
            var definitions = add.Concat(update).Select(item => item.IUWrapper)
                .Where(template => template != null && !template.IsPersisted)
                .Distinct<DbItemUnique>(ReferenceEqualityComparer.Instance).ToArray();
            if (definitions.Length != 0 && !GameServer.Database.AddObject(definitions))
                return false;

            bool saved = remove.Count == 0 || GameServer.Database.DeleteObject(remove);
            if (saved && update.Count > 0 && !GameServer.Database.SaveObject(update))
                saved = ReinsertMissingRows(update, add);
            if (saved && add.Count > 0 && !GameServer.Database.AddObject(add))
            {
                // A previous partially successful batch may have written a row
                // before its in-memory persisted flag was acknowledged. Reconcile
                // only those failed inserts, never overwrite a different item.
                saved = false;
                foreach (DbInventoryItem item in add.Where(item => !item.IsPersisted && !string.IsNullOrWhiteSpace(item.ObjectId)))
                {
                    DbInventoryItem stored = GameServer.Database.FindObjectByKey<DbInventoryItem>(item.ObjectId);
                    if (!CanReconcileSavedItem(item, stored)) continue;
                    item.IsPersisted = true;
                    item.Dirty = true; // Persist current slot on the next ordinary flush.
                }
            }
            if (!saved)
                return false;

            // Clear only the exact deletion records covered by the successful
            // batch. A concurrent later removal remains queued for the next run.
            foreach ((BotInventory inventory, List<DbInventoryItem> deleted) in captures)
            {
                lock (inventory.Lock)
                    foreach (DbInventoryItem item in deleted)
                        inventory._itemsAwaitingDeletion.Remove(item);
            }
            return true;
            }
            finally
            {
                // Acknowledge each successful row even if a later row failed.
                // Never clear live dirtiness: a newer slot/count/charge change
                // still belongs to the next regular flush.
                foreach (var entry in written)
                    lock (entry.Inventory.Lock)
                    {
                        if (entry.Snapshot.IsPersisted)
                        {
                            entry.Live.IsPersisted = true;
                            entry.Live.AcceptPersistenceBaseline(entry.Snapshot);
                        }
                        entry.Inventory._itemsBeingSaved.Remove(entry.Live);
                        if (!entry.Snapshot.IsPersisted && !entry.Live.IsPersisted)
                            entry.Inventory._itemsAwaitingDeletion.Remove(entry.Live);
                    }
                foreach (var entry in deletedCopies)
                    if (!entry.Snapshot.IsPersisted || entry.Snapshot.IsDeleted)
                        entry.Live.IsPersisted = false;
            }
        }

        public static bool CanReconcileSavedItem(DbInventoryItem item, DbInventoryItem stored) =>
            item != null && stored != null && !string.IsNullOrWhiteSpace(item.ObjectId) &&
            item.ObjectId == stored.ObjectId && !string.IsNullOrWhiteSpace(item.OwnerID) &&
            item.OwnerID == stored.OwnerID && item.OwnerLot == 0 && stored.OwnerLot == 0 &&
            item.ITemplate_Id == stored.ITemplate_Id && item.UTemplate_Id == stored.UTemplate_Id && item.Count == stored.Count;

        /// <summary>Of two slots holding the same item object, the one kept: the lowest (worn before bag before vault).</summary>
        public static eInventorySlot KeepDuplicateSlot(eInventorySlot first, eInventorySlot second) => first <= second ? first : second;

        /// <summary>
        /// owner 2026-11-10: one item object sitting in two slots (a loot bag taken twice) was saved as two rows, could be
        /// sold twice, and its sale deleted the row of the copy still worn (Gloves of Precision: 79 failed saves, gone on
        /// restart). Keeps <see cref="KeepDuplicateSlot"/> and clears the other slots; nothing is lost, it is one item.
        /// Returns the row ids this inventory holds. Caller holds Lock.
        /// </summary>
        private HashSet<string> DropDuplicateSlotsLocked(string ownerId)
        {
            var heldIds = new HashSet<string>(m_items.Count);
            var keptSlot = new Dictionary<DbInventoryItem, eInventorySlot>(m_items.Count, ReferenceEqualityComparer.Instance);
            List<(DbInventoryItem Item, eInventorySlot Slot)> extra = null;
            foreach ((eInventorySlot slot, DbInventoryItem item) in m_items)
            {
                if (item == null)
                    continue;
                if (item.ObjectId != null)
                    heldIds.Add(item.ObjectId);
                if (!keptSlot.TryGetValue(item, out eInventorySlot other))
                {
                    keptSlot[item] = slot;
                    continue;
                }
                eInventorySlot keep = KeepDuplicateSlot(other, slot);
                keptSlot[item] = keep;
                extra ??= new List<(DbInventoryItem Item, eInventorySlot Slot)>();
                extra.Add((item, keep == other ? slot : other));
            }
            if (extra == null)
                return heldIds;
            foreach ((DbInventoryItem item, eInventorySlot slot) in extra)
            {
                m_items.Remove(slot);
                eInventorySlot keep = keptSlot[item];
                item.SlotPosition = (int)keep;
                Log.Warn($"BOT_INVENTORY_DUPLICATE_SLOT owner={ownerId} id={item.ObjectId} item=\"{item.Name}\" kept={(int)keep} cleared={(int)slot}");
            }
            return heldIds;
        }

        /// <summary>
        /// owner 2026-11-10: the tombstones to delete. A removed item that this bot still holds in another slot, or that
        /// another owner has taken since (its OwnerID is theirs), keeps its row. Caller holds Lock.
        /// </summary>
        private List<DbInventoryItem> TakeDeletionsLocked(string ownerId, HashSet<string> heldIds)
        {
            var deleted = new List<DbInventoryItem>();
            for (int i = _itemsAwaitingDeletion.Count - 1; i >= 0; i--)
            {
                DbInventoryItem item = _itemsAwaitingDeletion[i];
                if (item == null || !item.IsPersisted)
                    continue;
                string holder = item.ObjectId != null && heldIds.Contains(item.ObjectId) ? ownerId
                    : !string.IsNullOrEmpty(item.OwnerID) && item.OwnerID != ownerId ? item.OwnerID : null;
                if (holder == null)
                {
                    deleted.Add(item);
                    continue;
                }
                _itemsAwaitingDeletion.RemoveAt(i);
                Log.Warn($"BOT_INVENTORY_DELETE_SKIPPED owner={ownerId} id={item.ObjectId} item=\"{item.Name}\" holder={holder}: still held, row kept");
            }
            return deleted;
        }

        /// <summary>
        /// owner 2026-11-10: an UPDATE that touched no row means the row is gone while the bot still holds the item; it
        /// was retried every flush and the item was lost on restart. Such items are inserted again in this batch. Any other
        /// failed update still fails the batch. Runs only after a failed update (one key lookup per failed row).
        /// </summary>
        private static bool ReinsertMissingRows(List<DbInventoryItem> update, List<DbInventoryItem> add)
        {
            bool onlyMissing = true;
            int missing = 0;
            foreach (DbInventoryItem snapshot in update)
            {
                // A written row is clean again; an unchanged one was never sent.
                if (!snapshot.Dirty)
                    continue;
                if (string.IsNullOrWhiteSpace(snapshot.ObjectId) ||
                    GameServer.Database.FindObjectByKey<DbInventoryItem>(snapshot.ObjectId) != null)
                {
                    onlyMissing = false;
                    continue;
                }
                snapshot.IsPersisted = false;
                add.Add(snapshot);
                missing++;
                Log.Warn($"BOT_INVENTORY_ROW_MISSING owner={snapshot.OwnerID} id={snapshot.ObjectId} item=\"{snapshot.Name}\" slot={snapshot.SlotPosition}: inserted again");
            }
            bool recovered = MissingRowsExplainFailedUpdate(missing, !onlyMissing);
            if (!recovered)
                Log.Warn($"BOT_INVENTORY_UPDATE_FAILED rows={update.Count} missing={missing} otherFailed={!onlyMissing}: batch failed");
            return recovered;
        }

        /// <summary>
        /// Pure rule (owner 2026-11-10, review): a failed update counts as recovered only when at least one row was found
        /// missing (and is inserted again) and nothing else failed. A failed relation save or a rolled-back commit leaves
        /// no dirty row behind, and still fails the batch.
        /// </summary>
        public static bool MissingRowsExplainFailedUpdate(int missingRows, bool otherRowFailed) =>
            missingRows > 0 && !otherRowFailed;

        public override bool AddItem(eInventorySlot slot, DbInventoryItem item)
        {
            lock (Lock)
            {
                // owner 2026-11-10: the same item object never goes into a second slot (see DropDuplicateSlotsLocked)
                if (item != null && TryFindSlotLocked(item, out eInventorySlot heldSlot))
                {
                    Log.Warn($"BOT_INVENTORY_DUPLICATE_ADD owner={_persistenceOwnerId} id={item.ObjectId} item=\"{item.Name}\" held={(int)heldSlot} refused={(int)slot}");
                    return false;
                }
                if (!base.AddItem(slot, item))
                    return false;
                _itemsAwaitingDeletion.Remove(item);
                if (IsPersistent)
                    item.OwnerID = _persistenceOwnerId;
                return true;
            }
        }

        private bool TryFindSlotLocked(DbInventoryItem item, out eInventorySlot slot)
        {
            foreach ((eInventorySlot held, DbInventoryItem other) in m_items)
            {
                if (ReferenceEquals(other, item))
                {
                    slot = held;
                    return true;
                }
            }
            slot = eInventorySlot.Invalid;
            return false;
        }

        public override bool RemoveItem(DbInventoryItem item)
        {
            return RemoveItem(item, true);
        }

        public override bool RemoveItemWithoutDbDeletion(DbInventoryItem item)
        {
            return RemoveItem(item, false);
        }

        private bool RemoveItem(DbInventoryItem item, bool markForDeletion)
        {
            lock (Lock)
            {
                if (item == null || !base.RemoveItem(item))
                    return false;

                if (markForDeletion && IsPersistent)
                {
                    // A removal can race a not-yet-acknowledged INSERT. Retain
                    // its tombstone so that successful insert is deleted later.
                    if ((item.IsPersisted || _itemsBeingSaved.Contains(item)) && !_itemsAwaitingDeletion.Contains(item))
                        _itemsAwaitingDeletion.Add(item);
                }
                return true;
            }
        }

        protected override eInventorySlot GetValidInventorySlot(eInventorySlot slot)
        {
            switch (slot)
            {
                case eInventorySlot.LastEmptyQuiver:
                    slot = FindLastEmptySlot(eInventorySlot.FirstQuiver, eInventorySlot.FourthQuiver);
                    break;
                case eInventorySlot.FirstEmptyQuiver:
                    slot = FindFirstEmptySlot(eInventorySlot.FirstQuiver, eInventorySlot.FourthQuiver);
                    break;
                case eInventorySlot.LastEmptyVault:
                    slot = FindLastEmptySlot(eInventorySlot.FirstVault, eInventorySlot.LastVault);
                    break;
                case eInventorySlot.FirstEmptyVault:
                    slot = FindFirstEmptySlot(eInventorySlot.FirstVault, eInventorySlot.LastVault);
                    break;
                case eInventorySlot.LastEmptyBackpack:
                    slot = FindLastEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                    break;
                case eInventorySlot.FirstEmptyBackpack:
                    slot = FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                    break;
                case eInventorySlot.LastEmptyBagHorse:
                    slot = FindLastEmptySlot(eInventorySlot.FirstBagHorse, eInventorySlot.LastBagHorse);
                    break;
                case eInventorySlot.FirstEmptyBagHorse:
                    slot = FindFirstEmptySlot(eInventorySlot.FirstBagHorse, eInventorySlot.LastBagHorse);
                    break;
            }

            if ((slot >= eInventorySlot.FirstBackpack && slot <= eInventorySlot.LastBackpack)
                || (slot >= eInventorySlot.HorseArmor && slot <= eInventorySlot.Horse)
                || (slot >= eInventorySlot.FirstVault && slot <= eInventorySlot.LastVault)
                || (slot >= eInventorySlot.HouseVault_First && slot <= eInventorySlot.HouseVault_Last)
                || (slot >= eInventorySlot.Consignment_First && slot <= eInventorySlot.Consignment_Last)
                || (slot == eInventorySlot.PlayerPaperDoll)
                || (slot == eInventorySlot.Mythical)
                || (slot >= eInventorySlot.FirstBagHorse && slot <= eInventorySlot.LastBagHorse))
                return slot;

            return base.GetValidInventorySlot(slot);
        }
    }
}
