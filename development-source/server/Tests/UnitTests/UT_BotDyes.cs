using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Owner 2026-11-10: gamebots dye and enamel their gear like players; richer bots splurge on dearer colours.</summary>
[TestFixture]
public class UT_BotDyes
{
    // ---- which dye fits which item (GamePlayerInventory.DyeItem)

    [TestCase(AutonomousBotDyes.ClothDyeModel, (int)eObjectType.Cloth, 25, true)]
    [TestCase(AutonomousBotDyes.ClothDyeModel, (int)eObjectType.Magical, 26, true)]     // cloak
    [TestCase(AutonomousBotDyes.ClothDyeModel, (int)eObjectType.Leather, 25, false)]
    [TestCase(AutonomousBotDyes.ClothDyeModel, (int)eObjectType.Magical, 29, false)]    // a magical necklace
    [TestCase(AutonomousBotDyes.LeatherDyeModel, (int)eObjectType.Leather, 25, true)]
    [TestCase(AutonomousBotDyes.LeatherDyeModel, (int)eObjectType.Cloth, 25, false)]
    [TestCase(AutonomousBotDyes.LeatherDyeModel, (int)eObjectType.Studded, 25, false)]
    [TestCase(AutonomousBotDyes.EnamelModel, (int)eObjectType.Studded, 25, true)]
    [TestCase(AutonomousBotDyes.EnamelModel, (int)eObjectType.Chain, 25, true)]
    [TestCase(AutonomousBotDyes.EnamelModel, (int)eObjectType.Plate, 25, true)]
    [TestCase(AutonomousBotDyes.EnamelModel, (int)eObjectType.Reinforced, 25, true)]
    [TestCase(AutonomousBotDyes.EnamelModel, (int)eObjectType.Scale, 25, true)]
    [TestCase(AutonomousBotDyes.EnamelModel, (int)eObjectType.Shield, 11, true)]
    [TestCase(AutonomousBotDyes.EnamelModel, (int)eObjectType.Leather, 25, false)]
    [TestCase(AutonomousBotDyes.EnamelModel, (int)eObjectType.Cloth, 25, false)]
    [TestCase(AutonomousBotDyes.EnamelModel, (int)eObjectType.Magical, 26, false)]      // no enamel on a cloak
    [TestCase(AutonomousBotDyes.WeaponLusterModel, (int)eObjectType.SlashingWeapon, 10, true)]
    [TestCase(AutonomousBotDyes.WeaponLusterModel, (int)eObjectType.Plate, 25, false)]
    [TestCase(100, (int)eObjectType.Cloth, 25, false)]                                  // not a dye model
    public void DyeKindFitsItemType(int model, int objectType, int itemType, bool fits)
    {
        Assert.That(AutonomousBotDyes.DyeFits(model, objectType, itemType), Is.EqualTo(fits));
    }

    [TestCase("light blue cloth dye", 229, 51, 50, true)]
    [TestCase("royal blue leather dye", 494, 30, 1375, true)]
    [TestCase("black enamel", 495, 37, 165000, true)]
    [TestCase("cloth bleach", 229, 1, 250, false)]
    [TestCase("mild acid wash", 494, 1, 250, false)]
    [TestCase("mild acid enamel remover", 495, 1, 250, false)]
    [TestCase("green hull paint", 229, 5, 100, false)]
    [TestCase("giant blood", 494, 0, 40, false)]             // spellcraft item with a dye model
    [TestCase("stout dwarven ale", 495, 0, 100, false)]
    [TestCase("Cloth Dye", 229, 0, 0, false)]                // quest item: no colour, no price
    [TestCase("blue dye", 100, 5, 100, false)]               // not a dye model
    public void OnlyColourDyesAreBought(string name, int model, int color, long price, bool expected)
    {
        Assert.That(AutonomousBotDyes.IsColourDye(name, model, color, price), Is.EqualTo(expected));
    }

    // ---- wealth -> chance and price tier

    [TestCase(0, 0)]
    [TestCase(9_999, 0)]
    [TestCase(10_000, 1)]
    [TestCase(99_999, 1)]
    [TestCase(100_000, 2)]
    [TestCase(500_000, 3)]
    [TestCase(2_000_000, 4)]
    [TestCase(50_000_000, 4)]
    public void WealthTierByCoin(long coin, int tier)
    {
        Assert.That(AutonomousBotDyes.WealthTier(coin), Is.EqualTo(tier));
    }

    [Test]
    public void RicherBotsDyeMoreOftenAndRestLess()
    {
        for (int tier = 1; tier <= 4; tier++)
        {
            Assert.That(AutonomousBotDyes.DyeChance(tier), Is.GreaterThan(AutonomousBotDyes.DyeChance(tier - 1)));
            Assert.That(AutonomousBotDyes.CooldownMilliseconds(tier), Is.LessThan(AutonomousBotDyes.CooldownMilliseconds(tier - 1)));
            Assert.That(AutonomousBotDyes.PreferredFloor(tier), Is.GreaterThanOrEqualTo(AutonomousBotDyes.PreferredFloor(tier - 1)));
        }
        Assert.That(AutonomousBotDyes.DyeChance(0), Is.GreaterThan(0).And.LessThan(0.1), "poor bots rarely dye");
        Assert.That(AutonomousBotDyes.CooldownMilliseconds(0), Is.GreaterThanOrEqualTo(3_600_000L), "hours of play between dyes");
    }

    [Test]
    public void PoorBotsOnlySeeCheapColoursRichBotsSeeDearOnes()
    {
        Assert.That(AutonomousBotDyes.PriceCeiling(5_000, 50), Is.LessThan(1_375), "under 1 gold: light and common colours");
        Assert.That(AutonomousBotDyes.PriceCeiling(50_000, 50), Is.GreaterThanOrEqualTo(1_375).And.LessThan(6_875));
        Assert.That(AutonomousBotDyes.PriceCeiling(5_000_000, 50), Is.GreaterThanOrEqualTo(192_500), "a rich bot can afford any colour");
    }

    // ---- spend cap

    [TestCase(10_000, 0, 1_000)]
    [TestCase(10_000, 2_000, 800)]
    [TestCase(1_000, 2_000, 0)]       // below the reserve: nothing to spend
    [TestCase(0, 50, 0)]
    public void SpendCapIsATenthOfSpendableCoin(long coin, long reserve, long cap)
    {
        Assert.That(AutonomousBotDyes.SpendCap(coin, reserve), Is.EqualTo(cap));
    }

    [Test]
    public void CeilingNeverExceedsTheSpendCap()
    {
        foreach (long coin in new long[] { 0, 400, 3_000, 20_000, 150_000, 900_000, 3_000_000, 90_000_000 })
            Assert.That(AutonomousBotDyes.PriceCeiling(coin, 256), Is.LessThanOrEqualTo(AutonomousBotDyes.SpendCap(coin, 256)));
    }

    [Test]
    public void ChosenOfferIsAffordableAndRichBotsSplurge()
    {
        long[] prices = [50, 280, 1_375, 6_875, 33_000, 165_000];
        var random = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            long poor = AutonomousBotDyes.ChooseOffer(prices, p => p, 5_000, 50, random);
            Assert.That(poor, Is.LessThanOrEqualTo(AutonomousBotDyes.SpendCap(5_000, 50)).And.GreaterThan(0));
            long rich = AutonomousBotDyes.ChooseOffer(prices, p => p, 5_000_000, 50, random);
            Assert.That(rich, Is.GreaterThanOrEqualTo(33_000), "a rich bot keeps to the dear colours");
        }
    }

    [Test]
    public void NoOfferWhenNothingIsAffordable()
    {
        Assert.That(AutonomousBotDyes.ChooseOffer(new long[] { 1_375, 6_875 }, p => p, 2_000, 50, new Random(1)), Is.EqualTo(0));
        Assert.That(AutonomousBotDyes.ChooseOffer(Array.Empty<long>(), p => p, 9_000_000, 50, new Random(1)), Is.EqualTo(0));
    }

    [Test]
    public void ARichTierFallsBackToCheaperColoursWhenNoDearOneIsSold()
    {
        long pick = AutonomousBotDyes.ChooseOffer(new long[] { 50, 280 }, p => p, 5_000_000, 50, new Random(3));
        Assert.That(new long[] { 50, 280 }.Contains(pick));
    }

    // ---- emblem skip

    [Test]
    public void EmblemedCloakOrShieldIsNeverDyed()
    {
        Assert.That(AutonomousBotDyes.SkipForEmblem(itemEmblem: 5123, emblemSlot: true, shownGuildEmblem: 0), Is.True, "own emblem");
        Assert.That(AutonomousBotDyes.SkipForEmblem(itemEmblem: 0, emblemSlot: true, shownGuildEmblem: 5123), Is.True,
            "gamebot in a player guild: its cloak/shield shows the guild emblem");
        Assert.That(AutonomousBotDyes.SkipForEmblem(itemEmblem: 0, emblemSlot: false, shownGuildEmblem: 5123), Is.False,
            "body armour of a guild gamebot can be dyed");
        Assert.That(AutonomousBotDyes.SkipForEmblem(itemEmblem: 0, emblemSlot: true, shownGuildEmblem: 0), Is.False,
            "no guild emblem: the cloak can be dyed");
    }

    // ---- review fixes (owner 2026-11-10)

    [TestCase(eInventorySlot.Cloak, (int)eObjectType.Magical, true)]
    [TestCase(eInventorySlot.LeftHandWeapon, (int)eObjectType.Shield, true)]
    [TestCase(eInventorySlot.LeftHandWeapon, (int)eObjectType.SlashingWeapon, false)]   // off-hand weapon
    [TestCase(eInventorySlot.TorsoArmor, (int)eObjectType.Plate, false)]
    [TestCase(eInventorySlot.HeadArmor, (int)eObjectType.Shield, false)]                 // only the left hand shows it
    public void EmblemSlotIsTheSlotLookedAt(eInventorySlot slot, int objectType, bool expected)
    {
        Assert.That(AutonomousBotDyes.IsEmblemSlot(slot, objectType), Is.EqualTo(expected));
    }

    [Test]
    public void NothingDyedOnlyStartsTheShortRetry()
    {
        for (int tier = 0; tier <= 4; tier++)
        {
            Assert.That(AutonomousBotDyes.RestAfterVisit(true, tier), Is.EqualTo(AutonomousBotDyes.CooldownMilliseconds(tier)));
            Assert.That(AutonomousBotDyes.RestAfterVisit(false, tier), Is.EqualTo(AutonomousBotDyes.RetryMilliseconds));
            Assert.That(AutonomousBotDyes.RetryMilliseconds, Is.LessThan(AutonomousBotDyes.CooldownMilliseconds(tier)));
        }
        Assert.That(AutonomousBotDyes.RetryMilliseconds, Is.GreaterThanOrEqualTo(10 * 60_000L), "retries stay rate-limited");
    }

    [Test]
    public void ForgetDropsTheBotsDyeRest()
    {
        var map = (ConcurrentDictionary<long, long>)typeof(AutonomousBotDyes)
            .GetField("NextDyeTick", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        const long botId = -987_654_321;   // no real bot uses a negative id
        map[botId] = 12_345;
        AutonomousBotDyes.Forget(botId);
        Assert.That(map.ContainsKey(botId), Is.False);
        Assert.DoesNotThrow(() => AutonomousBotDyes.Forget(botId), "forgetting twice is harmless");
    }
}
