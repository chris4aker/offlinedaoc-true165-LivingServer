using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.ServerRules;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DOL.UnitTests.Gameserver;

/// <summary>
/// 2026-11-11 audit: gamebots bought gear they already owned - a Savage's shields (its left hand is kept empty) and a
/// Minstrel's flutes (a song left its drum in the two-hand slot).
/// </summary>
[TestFixture, NonParallelizable]
public class UT_BotRebuyOwnedGear
{
    private sealed class RebuyTestServer : GameServer
    {
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private static readonly IObjectDatabase EmptyDatabase =
        DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    private GameServer _previousServer;
    private PetTestLanguageScope _language;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        _language = new PetTestLanguageScope();
        GameServer.LoadTestDouble((RebuyTestServer)RuntimeHelpers.GetUninitializedObject(typeof(RebuyTestServer)));
    }

    [TearDown]
    public void TearDown()
    {
        GameServer.LoadTestDouble(_previousServer);
        _language.Dispose();
    }

    [Test]
    public void TwoHandedSavageNeverBuysAShieldForItsEmptyLeftHand()
    {
        var savage = ShopBot<SavageShopBot>(eObjectType.Hammer);
        DbInventoryItem shield = Shield("rowan round shield", 10);
        Assert.Multiple(() =>
        {
            Assert.That(savage.Inventory.GetItem(eInventorySlot.LeftHandWeapon), Is.Null);
            Assert.That(GameServer.ServerRules.CheckAbilityToUseItem(savage, shield.Template), Is.True,
                "the Savage may carry the shield; only the hand rule says no");
            Assert.That(AutonomousBotEconomy.KeepsSlotForOtherUse(savage, shield, eInventorySlot.LeftHandWeapon), Is.True);
            Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(savage, shield, out _), Is.False);
            Assert.That(AutonomousBotEconomy.IsWorthBuying(savage, shield, out _), Is.False);
        });
    }

    [Test]
    public void HandToHandSavageKeepsItsLeftHandForHandToHandWeapons()
    {
        var savage = ShopBot<SavageShopBot>(eObjectType.HandToHand);
        DbInventoryItem claw = GameInventoryItem.Create(new DbItemTemplate
        {
            Id_nb = "test_claw", Name = "claw", Level = 10, Object_Type = (int)eObjectType.HandToHand,
            Item_Type = (int)eInventorySlot.LeftHandWeapon, Hand = 2, DPS_AF = 30, SPD_ABS = 30, Quality = 90,
            Condition = 50000, MaxCondition = 50000, Durability = 50000, MaxDurability = 50000,
        });
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotEconomy.KeepsSlotForOtherUse(savage, Shield("round shield", 10),
                eInventorySlot.LeftHandWeapon), Is.True);
            Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(savage, Shield("round shield", 10), out _), Is.False);
            Assert.That(AutonomousBotEconomy.KeepsSlotForOtherUse(savage, claw, eInventorySlot.LeftHandWeapon), Is.False);
        });
    }

    [Test]
    public void OtherClassesStillTakeAShieldButNotACopyOfTheOneInTheVault()
    {
        var warrior = ShopBot<WarriorShopBot>(eObjectType.Sword);
        DbInventoryItem shield = Shield("rowan round shield", 10);
        Assert.That(AutonomousBotEconomy.KeepsSlotForOtherUse(warrior, shield, eInventorySlot.LeftHandWeapon), Is.False);
        Assert.That(AutonomousBotEconomy.IsWorthBuying(warrior, shield, out var slot), Is.True, "an empty shield hand");
        Assert.That(slot, Is.EqualTo(eInventorySlot.LeftHandWeapon));

        warrior.Inventory.AddItem(eInventorySlot.FirstVault, Shield("rowan round shield", 10));
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(warrior, shield, out _), Is.True,
                "the worn-slot check alone still sees the empty hand");
            Assert.That(AutonomousBotEconomy.IsWorthBuying(warrior, shield, out _), Is.False,
                "the same shield already sits in the vault");
            Assert.That(AutonomousBotEconomy.IsWorthBuying(warrior, Shield("oak tower shield", 20), out _), Is.True,
                "a clearly better shield is still bought");
        });
    }

    [Test]
    public void MinstrelComparesAFluteWithItsOwnFlutesNotTheDrumASongLeftInTheSlot()
    {
        var minstrel = ShopBot<MinstrelShopBot>(eObjectType.Sword);
        minstrel.Inventory.AddItem(eInventorySlot.TwoHandWeapon, Instrument("oaken drum", eInstrumentType.Drum, 5));
        DbInventoryItem shopFlute = Instrument("ironwood flute", eInstrumentType.Flute, 15);

        Assert.That(AutonomousBotEconomy.IsWorthBuying(minstrel, shopFlute, out _), Is.True, "it owns no flute yet");

        minstrel.Inventory.AddItem(eInventorySlot.FirstVault, Instrument("ironwood flute", eInstrumentType.Flute, 15));
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(minstrel, shopFlute, out _), Is.False,
                "the same flute is in the vault; the drum in the slot does not count");
            Assert.That(AutonomousBotEconomy.IsWorthBuying(minstrel, shopFlute, out _), Is.False);
            Assert.That(AutonomousBotEconomy.IsWorthBuying(minstrel, Instrument("silver flute", eInstrumentType.Flute, 30), out _),
                Is.True, "a clearly better flute is still bought");
            Assert.That(AutonomousBotEconomy.IsWorthBuying(minstrel, Instrument("oaken drum", eInstrumentType.Drum, 5), out _),
                Is.False, "nor a second copy of the drum it holds");
        });
    }

    [Test]
    public void AnInstrumentTheBotOwnsIsNoUpgradeToSwapIntoTheSlot()
    {
        var minstrel = ShopBot<MinstrelShopBot>(eObjectType.Sword);
        minstrel.Inventory.AddItem(eInventorySlot.TwoHandWeapon, Instrument("oaken drum", eInstrumentType.Drum, 5));
        DbInventoryItem carried = Instrument("silver flute", eInstrumentType.Flute, 30);
        minstrel.Inventory.AddItem(eInventorySlot.FirstBackpack, carried);
        Assert.That(AutonomousBotEconomy.TryGetEquipmentUpgrade(minstrel, carried, out _), Is.False,
            "the song takes it from the bag; equipping it only pushes the drum out");
    }

    [Test]
    public void InstrumentHasNoWeaponSpec()
    {
        Assert.That(SkillBase.ObjectTypeToSpec(eObjectType.Instrument), Is.Null);
    }

    private static T ShopBot<T>(eObjectType weapon) where T : GameBot
    {
        var bot = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        bot.Level = 20;
        bot.Inventory = new BotInventory();
        typeof(GameLiving).GetField("m_abilities", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(bot, new Dictionary<string, Ability>
            {
                [Abilities.Shield] = new Ability(new DbAbility { KeyName = Abilities.Shield, Name = "Shield" }, 3),
            });
        typeof(GameLiving).GetField("_abilitiesLock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(bot, new System.Threading.Lock());
        BotSpec plan = typeof(T) == typeof(SavageShopBot)
            ? new SavageBotSpec(weapon == eObjectType.HandToHand ? eSpecType.DualWield : eSpecType.TwoHanded, weapon)
            : new BotSpec { WeaponOneType = weapon };
        typeof(GameBot).GetProperty(nameof(GameBot.BotSpec),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(bot, plan);
        return bot;
    }

    private static DbInventoryItem Shield(string name, int level) => GameInventoryItem.Create(new DbItemTemplate
    {
        Id_nb = "test_" + name.Replace(' ', '_'), Name = name, Level = level, Object_Type = (int)eObjectType.Shield,
        Item_Type = (int)eInventorySlot.LeftHandWeapon, Type_Damage = 2, DPS_AF = level, SPD_ABS = 40, Quality = 90,
        Condition = 50000, MaxCondition = 50000, Durability = 50000, MaxDurability = 50000,
    });

    private static DbInventoryItem Instrument(string name, eInstrumentType type, int level) => GameInventoryItem.Create(
        new DbItemTemplate
        {
            Id_nb = "test_" + name.Replace(' ', '_'), Name = name, Level = level, Object_Type = (int)eObjectType.Instrument,
            Item_Type = (int)eInventorySlot.TwoHandWeapon, DPS_AF = (int)type, Quality = 90,
            Condition = 50000, MaxCondition = 50000, Durability = 50000, MaxDurability = 50000,
        });

    private sealed class SavageShopBot : GameBot
    {
        private SavageShopBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override ICharacterClass CharacterClass => new ClassSavage();
        public override bool HasAbility(string keyName) => true;
        public override bool HasAbilityToUseItem(DbItemTemplate item) => true;
        public override int WeaponSpecLevel(eObjectType type, int slotPosition) => 5;
    }

    private sealed class WarriorShopBot : GameBot
    {
        private WarriorShopBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override ICharacterClass CharacterClass => new ClassWarrior();
        public override bool HasAbility(string keyName) => true;
        public override bool HasAbilityToUseItem(DbItemTemplate item) => true;
    }

    private sealed class MinstrelShopBot : GameBot
    {
        private MinstrelShopBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override ICharacterClass CharacterClass => new ClassMinstrel();
        public override bool HasAbility(string keyName) => true;
        public override bool HasAbilityToUseItem(DbItemTemplate item) => true;
    }
}
