using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot inventory integrity (owner 2026-11-10, server logs of 11-09/11-10): a loot bag taken twice put one item object
    /// in two slots or two bots, which failed saves with "UNIQUE constraint failed: Inventory.Inventory_ID", let the item
    /// be sold twice, and lost a worn item on restart. Each test uses its own new SQLite file, never the owner's world.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotInventoryIntegrity
    {
        private sealed class IntegrityServer : GameServer
        {
            public IObjectDatabase TestDatabase;
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => TestDatabase;
        }

        private sealed class TakingOwner : IGameStaticItemOwner
        {
            public int Items;
            public string Name => "taker";
            public object GameStaticItemOwnerComparand => this;
            public TryPickUpResult TryAutoPickUpMoney(GameMoney money) => TryPickUpResult.DoesNotWant;
            public TryPickUpResult TryAutoPickUpItem(WorldInventoryItem item) { Items++; return TryPickUpResult.Success; }
            public TryPickUpResult TryPickUpMoney(GamePlayer source, GameMoney money) => TryPickUpResult.DoesNotWant;
            public TryPickUpResult TryPickUpItem(GamePlayer source, WorldInventoryItem item) { Items++; return TryPickUpResult.Success; }
        }

        private GameServer _previousServer;
        private string _path;
        private SqliteObjectDatabase _database;

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            _path = Path.Combine(Path.GetTempPath(), "daoc-inventory-integrity-" + Guid.NewGuid().ToString("N") + ".sqlite3");
            _database = new SqliteObjectDatabase($"Data Source={_path};Version=3;Pooling=False;");
            _database.RegisterDataObject(typeof(DbItemTemplate));
            _database.RegisterDataObject(typeof(DbItemUnique));
            _database.RegisterDataObject(typeof(DbInventoryItem));
            var server = (IntegrityServer)RuntimeHelpers.GetUninitializedObject(typeof(IntegrityServer));
            server.TestDatabase = _database;
            GameServer.LoadTestDouble(server);
        }

        [TearDown]
        public void TearDown()
        {
            GameServer.LoadTestDouble(_previousServer);
            // Only the exact GUID-named file created by this test is removed.
            if (File.Exists(_path)) File.Delete(_path);
        }

        private static DbInventoryItem NewItem()
        {
            var template = new DbItemTemplate { Id_nb = Guid.NewGuid().ToString(), Name = "cloth pants",
                Realm = (int)eRealm.Hibernia, Level = 1, Item_Type = (int)eInventorySlot.LegsArmor,
                Object_Type = (int)eObjectType.Cloth, Quality = 95, MaxCount = 1, PackSize = 1,
                MaxCondition = 10000, MaxDurability = 10000, DPS_AF = 1, Price = 100 };
            return GameInventoryItem.Create(new DbItemUnique(template));
        }

        private static Dictionary<eInventorySlot, DbInventoryItem> Slots(BotInventory inventory) =>
            (Dictionary<eInventorySlot, DbInventoryItem>)typeof(GameLivingInventory)
                .GetField("m_items", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(inventory);

        private DbInventoryItem[] Rows() => _database.SelectAllObjects<DbInventoryItem>().ToArray();

        [TestCase(eInventorySlot.LegsArmor, eInventorySlot.FirstBackpack, eInventorySlot.LegsArmor)]
        [TestCase(eInventorySlot.LastBackpack, eInventorySlot.FirstBackpack, eInventorySlot.FirstBackpack)]
        [TestCase(eInventorySlot.FirstVault, eInventorySlot.FirstBackpack, eInventorySlot.FirstBackpack)]
        public void KeepDuplicateSlotKeepsTheLowestSlot(eInventorySlot first, eInventorySlot second, eInventorySlot kept)
        {
            Assert.That(BotInventory.KeepDuplicateSlot(first, second), Is.EqualTo(kept));
            Assert.That(BotInventory.KeepDuplicateSlot(second, first), Is.EqualTo(kept));
        }

        [Test]
        public void ABagTakenOnceCannotBeTakenAgain()
        {
            var drop = new WorldInventoryItem(NewItem());
            var first = new TakingOwner();
            var second = new TakingOwner();
            Assert.That(drop.TryAutoPickUp(first), Is.EqualTo(TryPickUpResult.Success));
            Assert.That(drop.TryAutoPickUp(second), Is.EqualTo(TryPickUpResult.DoesNotWant));
            Assert.That(drop.TryPickUp(null, second), Is.EqualTo(TryPickUpResult.DoesNotWant));
            Assert.That(first.Items, Is.EqualTo(1));
            Assert.That(second.Items, Is.EqualTo(0));
        }

        [Test]
        public void AddItemRefusesTheSameItemObjectInASecondSlot()
        {
            var inventory = new BotInventory("offlinebot:integrity-add");
            DbInventoryItem item = NewItem();
            Assert.That(inventory.AddItem(eInventorySlot.FirstBackpack, item), Is.True);
            Assert.That(inventory.AddItem(eInventorySlot.LegsArmor, item), Is.False);
            Assert.That(inventory.GetItem(eInventorySlot.LegsArmor), Is.Null);
            Assert.That(inventory.GetItem(eInventorySlot.FirstBackpack), Is.SameAs(item));
        }

        [Test]
        public void AllItemsIsACopySafeToScanWhileTheInventoryChanges()
        {
            var inventory = new BotInventory("offlinebot:integrity-copy");
            Assert.That(inventory.AddItem(eInventorySlot.FirstBackpack, NewItem()), Is.True);
            ICollection<DbInventoryItem> scan = inventory.AllItems;
            Assert.That(inventory.AddItem(eInventorySlot.FirstBackpack + 1, NewItem()), Is.True);
            Assert.That(scan.Count(item => item != null), Is.EqualTo(1));
            Assert.That(inventory.AllItems, Has.Count.EqualTo(2));
        }

        [Test]
        public void OneItemInTwoSlotsIsSavedOnceInTheLowerSlot()
        {
            const string owner = "offlinebot:integrity-slots";
            var inventory = new BotInventory(owner);
            DbInventoryItem item = NewItem();
            Assert.That(inventory.AddItem(eInventorySlot.FirstBackpack, item), Is.True);
            Slots(inventory)[eInventorySlot.LegsArmor] = item;
            Assert.That(inventory.SaveIntoDatabase(owner), Is.True);
            DbInventoryItem row = Rows().Single();
            Assert.That(row.ObjectId, Is.EqualTo(item.ObjectId));
            Assert.That(row.SlotPosition, Is.EqualTo((int)eInventorySlot.LegsArmor));
            Assert.That(inventory.GetItem(eInventorySlot.FirstBackpack), Is.Null);
            Assert.That(inventory.GetItem(eInventorySlot.LegsArmor), Is.SameAs(item));
        }

        [Test]
        public void OneItemHeldByTwoBotsIsInsertedOnceAndTheBatchSucceeds()
        {
            var first = new BotInventory("offlinebot:integrity-first");
            var second = new BotInventory("offlinebot:integrity-second");
            DbInventoryItem shared = NewItem();
            DbInventoryItem own = NewItem();
            Assert.That(first.AddItem(eInventorySlot.FirstBackpack, shared), Is.True);
            Assert.That(second.AddItem(eInventorySlot.FirstBackpack, shared), Is.True);
            Assert.That(second.AddItem(eInventorySlot.FirstBackpack + 1, own), Is.True);
            Assert.That(BotInventory.SaveManyIntoDatabase(new[]
            {
                (first, "offlinebot:integrity-first"),
                (second, "offlinebot:integrity-second"),
            }), Is.True);
            DbInventoryItem[] rows = Rows();
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows.Single(row => row.ObjectId == shared.ObjectId).OwnerID, Is.EqualTo("offlinebot:integrity-first"));
            Assert.That(rows.Single(row => row.ObjectId == own.ObjectId).OwnerID, Is.EqualTo("offlinebot:integrity-second"));
        }

        [Test]
        public void AHeldItemWhoseRowIsGoneIsInsertedAgain()
        {
            const string owner = "offlinebot:integrity-missing";
            var inventory = new BotInventory(owner);
            DbInventoryItem item = NewItem();
            Assert.That(inventory.AddItem(eInventorySlot.FirstBackpack, item), Is.True);
            Assert.That(inventory.SaveIntoDatabase(owner), Is.True);
            Assert.That(_database.DeleteObject(Rows().Single()), Is.True);
            Assert.That(inventory.MoveItem(eInventorySlot.FirstBackpack, eInventorySlot.LegsArmor, 1), Is.True);
            Assert.That(inventory.SaveIntoDatabase(owner), Is.True);
            DbInventoryItem row = Rows().Single();
            Assert.That(row.ObjectId, Is.EqualTo(item.ObjectId));
            Assert.That(row.SlotPosition, Is.EqualTo((int)eInventorySlot.LegsArmor));
            Assert.That(row.OwnerID, Is.EqualTo(owner));
        }

        [Test]
        public void SellingAStaleSecondSlotKeepsTheWornItemsRow()
        {
            const string owner = "offlinebot:integrity-sale";
            var inventory = new BotInventory(owner);
            DbInventoryItem item = NewItem();
            Assert.That(inventory.AddItem(eInventorySlot.FirstBackpack, item), Is.True);
            Assert.That(inventory.SaveIntoDatabase(owner), Is.True);
            Slots(inventory)[eInventorySlot.LegsArmor] = item;
            // A vendor sale removes the slot the item last saved in (the backpack); the worn copy stays.
            Assert.That(inventory.RemoveItem(item), Is.True);
            Assert.That(inventory.GetItem(eInventorySlot.LegsArmor), Is.SameAs(item));
            Assert.That(inventory.SaveIntoDatabase(owner), Is.True);
            DbInventoryItem row = Rows().Single();
            Assert.That(row.ObjectId, Is.EqualTo(item.ObjectId));
            Assert.That(row.SlotPosition, Is.EqualTo((int)eInventorySlot.LegsArmor));
            Assert.That(row.OwnerID, Is.EqualTo(owner));
        }

        [Test]
        public void ASoldItemNobodyHoldsIsStillDeleted()
        {
            const string owner = "offlinebot:integrity-sold";
            var inventory = new BotInventory(owner);
            DbInventoryItem item = NewItem();
            Assert.That(inventory.AddItem(eInventorySlot.FirstBackpack, item), Is.True);
            Assert.That(inventory.SaveIntoDatabase(owner), Is.True);
            Assert.That(inventory.RemoveItem(item), Is.True);
            Assert.That(inventory.SaveIntoDatabase(owner), Is.True);
            Assert.That(Rows(), Is.Empty);
        }
    }
}
