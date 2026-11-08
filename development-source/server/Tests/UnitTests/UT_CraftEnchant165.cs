using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Enchant helper (owner 2026-11-08): the real spellcraft imbue / overcharge formulas, the plan rules
    /// (owned gems first, own skill, cap, overcharge), the run state and the op 43 / 44 byte layouts (enchant_helper_spec).</summary>
    [TestFixture]
    public class UT_CraftEnchant165
    {
        private static ushort U16(byte[] p, int body) => BitConverter.ToUInt16(p, body - 4);
        private static uint U32(byte[] p, int body) => BitConverter.ToUInt32(p, body - 4);

        private static DbInventoryItem Item(int objectType = (int)eObjectType.Plate, int level = 51, int quality = 100, bool crafted = true)
        {
            return new DbInventoryItem { Object_Type = objectType, Level = level, Quality = quality, IsCrafted = crafted, Name = "chest" };
        }

        private static EnchantGemChoice Gem(eProperty type, int value, ushort id, int level = 500, int owned = 0, int ownedQuality = 0,
            long cost = 1000, int seconds = 10) => new()
        {
            RecipeId = id, BonusType = (int)type, BonusValue = value, RequiredLevel = level, Owned = owned, OwnedQuality = ownedQuality,
            MaterialCost = cost, Seconds = seconds, Name = $"gem {id}",
        };

        private static DbItemTemplate Material(string id, long price, int pack = 1) => new() { Id_nb = id, Name = id, Price = price, PackSize = pack };

        // ---- the real formulas

        [TestCase((int)eProperty.Strength, 1, 1)]
        [TestCase((int)eProperty.Strength, 28, 19)]
        [TestCase((int)eProperty.Quickness, 13, 9)]
        [TestCase((int)eProperty.MaxMana, 5, 8)]
        [TestCase((int)eProperty.MaxHealth, 28, 7)]
        [TestCase((int)eProperty.Resist_Heat, 5, 8)]
        [TestCase((int)eProperty.Skill_Slashing, 3, 10)]
        [TestCase((int)eProperty.Skill_Slashing, 1, 1)]
        [TestCase((int)eProperty.Focus_Darkness, 50, 1)]
        public void GemImbuePoints_AreTheRealFormula(int type, int value, int points) =>
            Assert.That(SpellCrafting.GemImbuePoints(type, value), Is.EqualTo(points));

        [TestCase(51, 100, 32)]
        [TestCase(50, 99, 28)]
        [TestCase(51, 94, 10)]
        [TestCase(40, 96, 14)]
        [TestCase(0, 100, 0)]
        public void ItemMaxImbuePoints_ByLevelAndQuality(int level, int quality, int max) =>
            Assert.That(SpellCrafting.ItemMaxImbuePoints(level, quality), Is.EqualTo(max));

        [Test]
        public void TotalImbuePoints_SumPlusBiggest_Halved_WithTheItemsOwnBonuses()
        {
            var item = Item();
            var four = new[] { ((int)eProperty.Strength, 28), (2, 28), (3, 28), (4, 28) };
            Assert.That(SpellCrafting.TotalImbuePoints(item, four), Is.EqualTo((19 * 4 + 19) / 2));

            item.Bonus1Type = (int)eProperty.Strength; item.Bonus1 = 28;
            Assert.That(SpellCrafting.TotalImbuePoints(item, new[] { ((int)eProperty.Resist_Heat, 5) }), Is.EqualTo((19 + 8 + 19) / 2));
            Assert.That(SpellCrafting.TotalImbuePoints(item, Array.Empty<(int, int)>()), Is.EqualTo(19));
            Assert.That(CraftEnchant165.ExistingImbue(item), Is.EqualTo((19, 19)));
        }

        [Test]
        public void OverchargeSuccess_IsKortsFormula()
        {
            Assert.That(SpellCrafting.OverchargeSuccessChance(100, 1000, 32, 33), Is.EqualTo(100), "capped at 100");
            Assert.That(SpellCrafting.OverchargeSuccessChance(100, 500, 32, 33), Is.EqualTo(98));   // 34+26-10+50, fudge -2
            Assert.That(SpellCrafting.OverchargeSuccessChance(96, 500, 20, 23), Is.EqualTo(53));    // 34+6-30+50, fudge -7
            Assert.That(SpellCrafting.OverchargeSuccessChance(100, 1000, 32, 37), Is.EqualTo(90));  // 34+26-70+100
            Assert.That(SpellCrafting.OverchargeSuccessChance(100, 1000, 32, 38), Is.EqualTo(0), "more than 5 over never works");
            Assert.That(SpellCrafting.OverchargeSuccessChance(90, 500, 32, 34), Is.EqualTo(100), "quality under 94 is not rolled");
            Assert.That(CraftEnchant165.SuccessChance(96, 100, 20, 20), Is.EqualTo(100), "not over: no roll");
        }

        [Test]
        public void PreserveChance_IsTheRealFormula()
        {
            Assert.That(SpellCrafting.PreserveChance(100, new[] { 94, 94 }, 500), Is.EqualTo(7));    // 0 + 2 x 3.6
            Assert.That(SpellCrafting.PreserveChance(100, new[] { 94, 94 }, 1000), Is.EqualTo(57));
            Assert.That(SpellCrafting.PreserveChance(99, new[] { 85 }, 300), Is.EqualTo(0), "never below 0");
            Assert.That(SpellCrafting.PreserveSkillModifier(49), Is.EqualTo(-50));
            Assert.That(SpellCrafting.PreserveSkillModifier(549), Is.EqualTo(0));
            Assert.That(SpellCrafting.PreserveSkillModifier(1000), Is.EqualTo(50));
        }

        [TestCase(80, 2)]
        [TestCase(94, 2)]
        [TestCase(97, 5)]
        [TestCase(100, 10)]
        public void TinctureCharges_ByQuality(int quality, int charges) =>
            Assert.That(Alchemy.MaxChargesForQuality(quality), Is.EqualTo(charges));

        // ---- item rules

        [Test]
        public void BonusSlots_CountInOrder()
        {
            var item = Item();
            Assert.That(CraftEnchant165.FreeBonusSlots(item), Is.EqualTo(4));
            item.Bonus1Type = 1; item.Bonus2Type = 2;
            Assert.That(CraftEnchant165.FreeBonusSlots(item), Is.EqualTo(2));
            Assert.That(CraftEnchant165.ExistingBonusTypes(item), Is.EqualTo(new[] { 1, 2 }));
            item.Bonus3Type = 3; item.Bonus4Type = 4;
            Assert.That(CraftEnchant165.FreeBonusSlots(item), Is.EqualTo(0));
        }

        [Test]
        public void TinctureFits_FollowsIsAllowedToCombine()
        {
            Assert.That(CraftEnchant165.TinctureFits((int)eObjectType.Sword, 1), Is.True);
            Assert.That(CraftEnchant165.TinctureFits((int)eObjectType.Sword, 0), Is.False);
            Assert.That(CraftEnchant165.TinctureFits((int)eObjectType.Plate, 0), Is.True);
            Assert.That(CraftEnchant165.TinctureFits((int)eObjectType.Plate, 1), Is.False);
            Assert.That(CraftEnchant165.TinctureFits((int)eObjectType.Shield, 0), Is.True);
            Assert.That(CraftEnchant165.TinctureFits((int)eObjectType.Shield, 1), Is.True);
            Assert.That(CraftEnchant165.TinctureFits((int)eObjectType.Instrument, 0), Is.True);
            Assert.That(CraftEnchant165.TinctureFits((int)eObjectType.Instrument, 1), Is.False);
            Assert.That(CraftEnchant165.TinctureFits((int)eObjectType.Plate, 2), Is.False);
        }

        [Test]
        public void TinctureKind_ProcOrCharges()
        {
            Assert.That(CraftEnchant165.KindOf(1, 92100, 0), Is.EqualTo(TinctureKind.WeaponProc));
            Assert.That(CraftEnchant165.KindOf(0, 92100, 0), Is.EqualTo(TinctureKind.ArmourProc));
            Assert.That(CraftEnchant165.KindOf(0, 0, 92100), Is.EqualTo(TinctureKind.Charges));
            Assert.That(CraftEnchant165.KindOf(0, 0, 0), Is.EqualTo(TinctureKind.None));
        }

        [Test]
        public void SameEnchantItem_GemsByBonus_TincturesByEffect()
        {
            var gem = new DbItemTemplate { Id_nb = "raw_fiery", Object_Type = (int)eObjectType.SpellcraftGem, Bonus1Type = 1, Bonus1 = 4 };
            Assert.That(CraftEnchant165.SameEnchantItem(new DbInventoryItem { Object_Type = (int)eObjectType.SpellcraftGem, Bonus1Type = 1, Bonus1 = 4 }, gem), Is.True);
            Assert.That(CraftEnchant165.SameEnchantItem(new DbInventoryItem { Object_Type = (int)eObjectType.SpellcraftGem, Bonus1Type = 1, Bonus1 = 7 }, gem), Is.False);
            var tincture = new DbItemTemplate { Object_Type = (int)eObjectType.AlchemyTincture, Type_Damage = 1, ProcSpellID = 92100, ProcChance = 10, LevelRequirement = 20 };
            Assert.That(CraftEnchant165.SameEnchantItem(new DbInventoryItem
                { Object_Type = (int)eObjectType.AlchemyTincture, Type_Damage = 1, ProcSpellID = 92100, ProcChance = 10, LevelRequirement = 20 }, tincture), Is.True);
            Assert.That(CraftEnchant165.SameEnchantItem(new DbInventoryItem
                { Object_Type = (int)eObjectType.AlchemyTincture, Type_Damage = 0, ProcSpellID = 92100, ProcChance = 10, LevelRequirement = 20 }, tincture), Is.False);
        }

        // ---- the spellcraft plan

        [Test]
        public void PlanSpell_OwnedGemsFirst_OnlyTheRestIsCraftedAndPaid()
        {
            var gems = new List<EnchantGemChoice>
            {
                Gem(eProperty.Strength, 16, 1, owned: 1, ownedQuality: 99, cost: 5000, seconds: 30),
                Gem((eProperty)2, 16, 2, cost: 700, seconds: 12),
                Gem((eProperty)3, 16, 3, cost: 300, seconds: 8),
            };
            EnchantPlan plan = CraftEnchant165.PlanSpell(Item(), gems, 1000, false, 94);
            Assert.That(plan.Ok, Is.True, plan.Why);
            Assert.That(plan.Owned, Is.EqualTo(new[] { 0 }));
            Assert.That(plan.Crafted, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(plan.MaterialCost, Is.EqualTo(1000));
            Assert.That(plan.Seconds, Is.EqualTo(20));
            Assert.That(plan.Total, Is.EqualTo((11 * 3 + 11) / 2));
            Assert.That((plan.Max, plan.Cap, plan.Over), Is.EqualTo((32, 50, 22 - 32)));
            Assert.That(plan.SuccessChance, Is.EqualTo(100));
            Assert.That(plan.ExplodeChance, Is.EqualTo(0));
        }

        [Test]
        public void PlanSpell_OwnSkillMustMakeEveryGemNotOwned()
        {
            var gems = new List<EnchantGemChoice> { Gem(eProperty.Strength, 4, 1, level: 600) };
            EnchantPlan plan = CraftEnchant165.PlanSpell(Item(), gems, 550, false, 94);
            Assert.That(plan.Ok, Is.False);
            Assert.That(plan.Reason, Is.EqualTo(EnchantReason.NotEnoughSkill));

            Assert.That(CraftEnchant165.PlanSpell(Item(), gems, 551, false, 94).Ok, Is.True, "49 under the recipe is red, not purple");
            gems[0].Owned = 1;
            Assert.That(CraftEnchant165.PlanSpell(Item(), gems, 550, false, 94).Ok, Is.True, "an owned gem needs no craft");
        }

        [Test]
        public void PlanSpell_SpellcrafterCap_IsSkillOver20()
        {
            var gems = new List<EnchantGemChoice> { Gem(eProperty.Strength, 28, 1, level: 50) };
            EnchantPlan plan = CraftEnchant165.PlanSpell(Item(), gems, 100, true, 94);   // 19 points, cap 5
            Assert.That(plan.Reason, Is.EqualTo(EnchantReason.NotEnoughSkill));
            Assert.That(CraftEnchant165.PlanSpell(Item(), gems, 380, true, 94).Ok, Is.True, "cap 19");
        }

        [Test]
        public void PlanSpell_Overcharge_OnlyWhenAllowed_AndWhileItCanSucceed()
        {
            var gems = new List<EnchantGemChoice>
            {
                Gem(eProperty.Strength, 28, 1), Gem((eProperty)2, 28, 2), Gem((eProperty)3, 13, 3), Gem(eProperty.Quickness, 13, 4),
            };
            EnchantPlan refused = CraftEnchant165.PlanSpell(Item(), gems, 1000, false, 94);
            Assert.That(refused.Ok, Is.False);
            Assert.That(refused.Reason, Is.EqualTo(EnchantReason.Other));

            EnchantPlan plan = CraftEnchant165.PlanSpell(Item(), gems, 1000, true, 94);
            Assert.That(plan.Ok, Is.True, plan.Why);
            Assert.That((plan.Total, plan.Over), Is.EqualTo((37, 5)));
            Assert.That(plan.SuccessChance, Is.EqualTo(90));
            Assert.That(plan.PreserveChance, Is.EqualTo(64));   // 50 + 4 x (94 - 92) x 1.8
            Assert.That(plan.ExplodeChance, Is.EqualTo(4));     // 10% fail x 36% no preserve

            gems[3] = Gem(eProperty.Quickness, 16, 4);          // 38 points: 6 over
            EnchantPlan tooFar = CraftEnchant165.PlanSpell(Item(), gems, 1000, true, 94);
            Assert.That(tooFar.Ok, Is.False);
            Assert.That(tooFar.Reason, Is.EqualTo(EnchantReason.Other));
        }

        [Test]
        public void PlanSpell_PreserveUsesTheOwnedGemsQuality()
        {
            var gems = new List<EnchantGemChoice>
            {
                Gem(eProperty.Strength, 28, 1, owned: 1, ownedQuality: 100), Gem((eProperty)2, 28, 2), Gem((eProperty)3, 13, 3), Gem(eProperty.Quickness, 13, 4),
            };
            EnchantPlan plan = CraftEnchant165.PlanSpell(Item(), gems, 1000, true, 94);
            Assert.That(plan.PreserveChance, Is.EqualTo((int)(50 + (8 + 2 + 2 + 2) * 1.8)));
        }

        [Test]
        public void PlanSpell_ItemAndSlotRules()
        {
            var one = new List<EnchantGemChoice> { Gem(eProperty.Strength, 4, 1, level: 50) };
            Assert.That(CraftEnchant165.PlanSpell(Item(crafted: false), one, 1000, false, 94).Reason, Is.EqualTo(EnchantReason.ItemGone));
            Assert.That(CraftEnchant165.PlanSpell(Item(objectType: (int)eObjectType.SpellcraftGem), one, 1000, false, 94).Reason, Is.EqualTo(EnchantReason.ItemGone));
            Assert.That(CraftEnchant165.PlanSpell(Item(), one, 0, false, 94).Reason, Is.EqualTo(EnchantReason.NotEnoughSkill));
            Assert.That(CraftEnchant165.PlanSpell(Item(), new List<EnchantGemChoice>(), 1000, false, 94).Ok, Is.False);

            var item = Item();
            item.Bonus1Type = (int)eProperty.Strength; item.Bonus1 = 4;
            item.Bonus2Type = 2; item.Bonus2 = 4;
            item.Bonus3Type = 3; item.Bonus3 = 4;
            var two = new List<EnchantGemChoice> { Gem(eProperty.Quickness, 4, 1, level: 50), Gem((eProperty)5, 4, 2, level: 50) };
            EnchantPlan plan = CraftEnchant165.PlanSpell(item, two, 1000, false, 94);
            Assert.That(plan.Ok, Is.False, "one free slot");
            Assert.That(CraftEnchant165.PlanSpell(item, one, 1000, false, 94).Why, Does.Contain("only once"), "strength is already on it");

            var dup = new List<EnchantGemChoice> { Gem(eProperty.Quickness, 4, 1, level: 50), Gem(eProperty.Quickness, 7, 2, level: 50) };
            Assert.That(CraftEnchant165.PlanSpell(Item(), dup, 1000, false, 94).Why, Does.Contain("only once"));
        }

        // ---- the alchemy plan

        private static EnchantTinctureChoice Tincture(int typeDamage, int proc, int charge, int level = 600, int owned = 0) => new()
        {
            RecipeId = 205, TypeDamage = typeDamage, ProcSpellID = proc, SpellID = charge, RequiredLevel = level, Owned = owned,
            MaterialCost = 14_180, Seconds = 9, Name = "tincture",
        };

        [Test]
        public void PlanAlchemy_Rules()
        {
            var sword = Item((int)eObjectType.Sword);
            Assert.That(CraftEnchant165.PlanAlchemy(sword, Tincture(1, 92100, 0), 700).Ok, Is.True);
            Assert.That(CraftEnchant165.PlanAlchemy(sword, Tincture(0, 92100, 0), 700).Ok, Is.False, "armour tincture on a sword");
            Assert.That(CraftEnchant165.PlanAlchemy(Item((int)eObjectType.Shield), Tincture(1, 92100, 0), 700).Ok, Is.True);
            Assert.That(CraftEnchant165.PlanAlchemy(Item((int)eObjectType.Instrument), Tincture(0, 0, 92100), 700).Ok, Is.True);
            Assert.That(CraftEnchant165.PlanAlchemy(sword, Tincture(1, 92100, 0), 550).Reason, Is.EqualTo(EnchantReason.NotEnoughSkill));
            Assert.That(CraftEnchant165.PlanAlchemy(sword, Tincture(1, 92100, 0), 0).Reason, Is.EqualTo(EnchantReason.NotEnoughSkill));

            EnchantPlan owned = CraftEnchant165.PlanAlchemy(sword, Tincture(1, 92100, 0, owned: 2), 550);
            Assert.That(owned.Ok, Is.True, "an owned tincture needs no skill to make");
            Assert.That((owned.Owned.Count, owned.Crafted.Count, owned.MaterialCost), Is.EqualTo((1, 0, 0L)));

            EnchantPlan made = CraftEnchant165.PlanAlchemy(sword, Tincture(1, 92100, 0), 700);
            Assert.That((made.Crafted.Count, made.MaterialCost, made.Seconds), Is.EqualTo((1, 14_180L, 9)));

            sword.ProcSpellID = 1;
            Assert.That(CraftEnchant165.PlanAlchemy(sword, Tincture(1, 92100, 0), 700).Reason, Is.EqualTo(EnchantReason.ItemGone), "one tincture per item");
        }

        // ---- header

        [Test]
        public void Header_Reasons()
        {
            Assert.That(CraftEnchant165.HeaderOf(null, 40, 500, 500).Reason, Is.EqualTo(EnchantBlock.NotAllowed));
            Assert.That(CraftEnchant165.HeaderOf(Item(crafted: false), 40, 500, 500).Reason, Is.EqualTo(EnchantBlock.NotAllowed));

            EnchantHeader ok = CraftEnchant165.HeaderOf(Item(), 41, 500, 500);
            Assert.That((ok.CanSpell, ok.CanAlchemy, ok.Reason, ok.FreeSlots, ok.ImbueMax, ok.Cap), Is.EqualTo((true, true, EnchantBlock.None, 4, 32, 25)));

            var full = Item();
            full.Bonus1Type = 1; full.Bonus2Type = 2; full.Bonus3Type = 3; full.Bonus4Type = 4; full.ProcSpellID = 5;
            Assert.That(CraftEnchant165.HeaderOf(full, 40, 500, 500).Reason, Is.EqualTo(EnchantBlock.NoSlotsHasProc));
            Assert.That(CraftEnchant165.HeaderOf(Item(), 40, 0, 0).Reason, Is.EqualTo(EnchantBlock.NoSkill));
            Assert.That(CraftEnchant165.HeaderOf(Item(level: 1, quality: 94), 40, 500, 0).Reason, Is.EqualTo(EnchantBlock.TooLowLevel));
        }

        // ---- money and parsing helpers

        [Test]
        public void MissingCost_WholePacks_MinusTheBackpack()
        {
            var gem = Material("Lo gem", 60);
            var essence = Material("essence of life", 60);
            var fire = Material("draconic fire", 40, pack: 10);
            var ingredients = new List<Ingredient> { new(1, gem), new(3, essence), new(12, fire) };
            var have = new Dictionary<string, int> { ["essence of life"] = 2 };
            Assert.That(CraftEnchant165.MissingCost(have, ingredients), Is.EqualTo(60 + 60 + 2 * 40));
            Assert.That(CraftEnchant165.MissingCost(null, ingredients), Is.EqualTo(60 + 180 + 80));
        }

        [Test]
        public void MaterialsObtainable_UnsoldMaterialsMustBeOwned()
        {
            var rare = Material("rare thing", 0);
            var ingredients = new List<Ingredient> { new(2, rare), new(1, Material("Lo gem", 60)) };
            Assert.That(CraftEnchant165.MaterialsObtainable(new Dictionary<string, int>(), ingredients), Is.False);
            Assert.That(CraftEnchant165.MaterialsObtainable(new Dictionary<string, int> { ["rare thing"] = 2 }, ingredients), Is.True);
        }

        [Test]
        public void MergeMissing_AddsTheSameMaterialOnce()
        {
            var essence = Material("essence_of_life", 60);
            var fire = Material("draconic_fire", 40);
            var merged = CraftEnchant165.MergeMissing(new[] { (essence, 3), (fire, 1), (essence, 4) });
            Assert.That(merged.Select(m => (m.Material.Id_nb, m.Need)), Is.EqualTo(new[] { ("essence_of_life", 7), ("draconic_fire", 1) }));
        }

        [TestCase("40", true, 40)]
        [TestCase("79", true, 79)]
        [TestCase("0", true, 40)]
        [TestCase("39", true, 79)]
        [TestCase("80", false, 0)]
        [TestCase("-1", false, 0)]
        [TestCase("x", false, 0)]
        public void BackpackSlot_Parsing(string text, bool ok, int slot)
        {
            Assert.That(CraftEnchant165.TryBackpackSlot(text, out int s), Is.EqualTo(ok));
            Assert.That(s, Is.EqualTo(slot));
        }

        [Test]
        public void RecipeIds_Parsing()
        {
            Assert.That(CraftEnchant165.TryRecipeIds("11201,11211", out var ids), Is.True);
            Assert.That(ids, Is.EqualTo(new ushort[] { 11201, 11211 }));
            Assert.That(CraftEnchant165.TryRecipeIds("1,2,3,4,5", out _), Is.False, "four bonuses at most");
            Assert.That(CraftEnchant165.TryRecipeIds("7,7", out _), Is.False);
            Assert.That(CraftEnchant165.TryRecipeIds("a", out _), Is.False);
            Assert.That(CraftEnchant165.TryRecipeIds("", out _), Is.False);
        }

        // ---- run state

        [Test]
        public void Run_EndsOnce_DoneFinishesTheSteps()
        {
            var run = new EnchantRun();
            Assert.That(run.Running, Is.False);
            run.Begin(false, true, "chest");
            run.StepsTotal = 3; run.StepIndex = 1; run.RecipeId = 9; run.SecondsLeft = 40;
            Assert.That(run.State, Is.EqualTo(EnchantState.Buying));
            Assert.That(run.End(EnchantReason.Done), Is.True);
            Assert.That((run.State, run.StepIndex, run.RecipeId, run.SecondsLeft), Is.EqualTo((EnchantState.Done, 3, (ushort)0, 0.0)));
            Assert.That(run.End(EnchantReason.Interrupted), Is.False, "a second end is ignored");
            Assert.That(run.Reason, Is.EqualTo(EnchantReason.Done));

            run.Begin(true, false, "sword");
            run.State = EnchantState.Crafting;
            Assert.That(run.Running, Is.True);
            run.End(EnchantReason.StoppedByPlayer);
            Assert.That((run.State, run.Reason, run.Alchemy), Is.EqualTo((EnchantState.Stopped, EnchantReason.StoppedByPlayer, true)));
        }

        [Test]
        public void Run_SecondsLeft_CountsTheCraftsNotMade()
        {
            var run = new EnchantRun();
            run.Begin(false, false, "x");
            run.Crafts = new List<EnchantCraft> { new() { ExpectedSeconds = 10 }, new() { ExpectedSeconds = 12.5 }, new() { ExpectedSeconds = 7 } };
            run.UpdateSecondsLeft();
            Assert.That(run.SecondsLeft, Is.EqualTo(29.5));
            run.NextCraft = 2;
            run.UpdateSecondsLeft();
            Assert.That(run.SecondsLeft, Is.EqualTo(7));
        }

        [TestCase(CraftAssistOutcome.Made, EnchantReason.None)]
        [TestCase(CraftAssistOutcome.Failed, EnchantReason.None)]
        [TestCase(CraftAssistOutcome.Interrupted, EnchantReason.Interrupted)]
        [TestCase(CraftAssistOutcome.None, EnchantReason.Interrupted)]
        [TestCase(CraftAssistOutcome.StoppedByPlayer, EnchantReason.StoppedByPlayer)]
        [TestCase(CraftAssistOutcome.MaterialsGone, EnchantReason.Other)]
        public void CraftOutcome_MapsToStopReason(CraftAssistOutcome outcome, EnchantReason reason) =>
            Assert.That(CraftEnchant165.ReasonFor(outcome), Is.EqualTo(reason));

        [TestCase(CraftAssistReason.None, EnchantReason.None)]
        [TestCase(CraftAssistReason.OutOfMoney, EnchantReason.OutOfMoney)]
        [TestCase(CraftAssistReason.BackpackFull, EnchantReason.BackpackFull)]
        [TestCase(CraftAssistReason.Other, EnchantReason.Other)]
        public void BuyResult_MapsToStopReason(CraftAssistReason buy, EnchantReason reason) =>
            Assert.That(CraftEnchant165.ReasonFor(buy), Is.EqualTo(reason));

        // ---- packets

        [Test]
        public void Header_ByteLayout()
        {
            var h = new EnchantHeader
            {
                Slot = 47, Level = 51, Quality = 99, FreeSlots = 3, HasProc = true, CanSpell = true, CanAlchemy = false,
                ImbueMax = 28, ImbueUsed = 9, SpellSkill = 1005, AlchemySkill = 640, Cap = 50, Reason = EnchantBlock.None, Name = "Crafted Chest",
            };
            byte[] p = CraftEnchant165.BuildHeader(h);
            Assert.That(p[0], Is.EqualTo(0));
            Assert.That((p[1], p[2], p[3], p[4], p[5], p[6], p[7]), Is.EqualTo(((byte)47, (byte)51, (byte)99, (byte)3, (byte)1, (byte)1, (byte)0)));
            Assert.That((U16(p, 12), U16(p, 14), U16(p, 16), U16(p, 18), U16(p, 20)), Is.EqualTo(((ushort)28, (ushort)9, (ushort)1005, (ushort)640, (ushort)50)));
            Assert.That(p[22 - 4], Is.EqualTo(0));
            Assert.That(Encoding.UTF8.GetString(p, 23 - 4, p.Length - (23 - 4) - 1), Is.EqualTo("Crafted Chest"));
            Assert.That(p[^1], Is.EqualTo(0), "zero-ended");
        }

        [Test]
        public void GemRows_ByteLayout_FivePerPacket()
        {
            var rows = Enumerable.Range(0, 7).Select(i => new EnchantGemRow((ushort)(11201 + i), 1, 28, 190, 300_000 + i, 25, 918, i, i % 2 == 0, 94)).ToList();
            List<byte[]> packets = CraftEnchant165.BuildGemRows(rows);
            Assert.That(packets.Count, Is.EqualTo(2));
            Assert.That(packets[0].Length, Is.EqualTo(2 + 5 * 20));
            Assert.That(packets.All(p => p.Length <= CraftEnchant165.MaxPayload), Is.True);
            byte[] p = packets[1];
            Assert.That((p[0], p[1]), Is.EqualTo(((byte)1, (byte)2)));
            // the second row of packet 2 (row 6) starts at body [6 + 20]
            int b = 6 + 20;
            Assert.That((U16(p, b), U16(p, b + 2), U16(p, b + 4), U16(p, b + 6)), Is.EqualTo(((ushort)11207, (ushort)1, (ushort)28, (ushort)190)));
            Assert.That(U32(p, b + 8), Is.EqualTo(300_006));
            Assert.That((U16(p, b + 12), U16(p, b + 14)), Is.EqualTo(((ushort)25, (ushort)918)));
            Assert.That((p[b + 16 - 4], p[b + 17 - 4], p[b + 18 - 4], p[b + 19 - 4]), Is.EqualTo(((byte)6, (byte)1, (byte)94, (byte)0)));
        }

        [Test]
        public void TinctureRows_ByteLayout()
        {
            var rows = new List<EnchantTinctureRow>
            {
                new(205, TinctureKind.WeaponProc, 0, 92100, 20, 1, 14_180, 9, 594, true, 10),
                new(239, TinctureKind.Charges, 7, 92101, 25, 0, 11_680, 8, 534, false, 0),
            };
            byte[] p = CraftEnchant165.BuildTinctureRows(rows).Single();
            Assert.That((p[0], p[1]), Is.EqualTo(((byte)2, (byte)2)));
            int b = 6;
            Assert.That(U16(p, b), Is.EqualTo(205));
            Assert.That((p[b + 2 - 4], p[b + 3 - 4]), Is.EqualTo(((byte)1, (byte)0)));
            Assert.That(U32(p, b + 4), Is.EqualTo(92100));
            Assert.That((p[b + 8 - 4], p[b + 9 - 4]), Is.EqualTo(((byte)20, (byte)1)));
            Assert.That(U32(p, b + 10), Is.EqualTo(14_180));
            Assert.That((U16(p, b + 14), U16(p, b + 16)), Is.EqualTo(((ushort)9, (ushort)594)));
            Assert.That((p[b + 18 - 4], p[b + 19 - 4]), Is.EqualTo(((byte)1, (byte)10)));
            b = 26;
            Assert.That((U16(p, b), p[b + 2 - 4], p[b + 3 - 4], p[b + 18 - 4]), Is.EqualTo(((ushort)239, (byte)3, (byte)7, (byte)0)));
        }

        [Test]
        public void Strings_FitThePacket_AndReadBack()
        {
            var entries = Enumerable.Range(0, 20)
                .Select(i => ((ushort)(200 + i), i % 2 == 0 ? $"volatile fire alloy weapon tincture {i}|Heat damage 45 on hit" : $"perfect Fiery Essence Jewel {i}"))
                .ToList();
            List<byte[]> packets = CraftEnchant165.BuildStrings(entries);
            Assert.That(packets.Count, Is.GreaterThan(1));
            var back = new List<(ushort, string)>();
            foreach (byte[] p in packets)
            {
                Assert.That(p.Length, Is.LessThanOrEqualTo(CraftEnchant165.MaxPayload));
                Assert.That(p[0], Is.EqualTo(3));
                int at = 2;
                for (int k = 0; k < p[1]; k++)
                {
                    ushort id = BitConverter.ToUInt16(p, at);
                    int end = Array.IndexOf(p, (byte)0, at + 2);
                    back.Add((id, Encoding.UTF8.GetString(p, at + 2, end - at - 2)));
                    at = end + 1;
                }
                Assert.That(at, Is.EqualTo(p.Length));
            }
            Assert.That(back, Is.EqualTo(entries));
        }

        [Test]
        public void LongNames_AreCut()
        {
            string longName = new string('x', 300);
            Assert.That(CraftEnchant165.NameBytes(longName).Length, Is.EqualTo(CraftEnchant165.MaxNameBytes));
            byte[] p = CraftEnchant165.BuildHeader(new EnchantHeader { Name = longName });
            Assert.That(p.Length, Is.LessThanOrEqualTo(CraftEnchant165.MaxPayload));
            var run = new EnchantRun { ItemName = longName };
            Assert.That(CraftEnchant165.BuildStatus(run).Length, Is.LessThanOrEqualTo(CraftEnchant165.MaxPayload));
        }

        [Test]
        public void ImbueRules_ByteLayout()
        {
            var item = Item(quality: 100);
            item.Bonus1Type = (int)eProperty.Strength; item.Bonus1 = 28;
            byte[] p = CraftEnchant165.BuildImbueRules(item, 1000, 94);
            Assert.That(p[0], Is.EqualTo(4));
            Assert.That((U16(p, 6), U16(p, 8)), Is.EqualTo(((ushort)19, (ushort)19)));
            Assert.That(p.Skip(6).Take(7).ToArray(), Is.EqualTo(new byte[] { 100, 100, 100, 100, 100, 90, 0 }));
            Assert.That((sbyte)p[13], Is.EqualTo(50));
            Assert.That((p[14], p[15], p[16]), Is.EqualTo(((byte)1, (byte)94, (byte)10)));
            Assert.That((sbyte)CraftEnchant165.BuildImbueRules(item, 100, 94)[13], Is.EqualTo(-40));
        }

        [Test]
        public void Status_ByteLayout()
        {
            var run = new EnchantRun();
            run.Begin(false, true, "Crafted Chest");
            run.State = EnchantState.Crafting;
            run.StepIndex = 2; run.StepsTotal = 5; run.Spent = 1_234_567; run.RecipeId = 11210; run.SecondsLeft = 61.6;
            byte[] p = CraftEnchant165.BuildStatus(run);
            Assert.That((p[0], p[1], p[2], p[3], p[4], p[5]), Is.EqualTo(((byte)2, (byte)0, (byte)0, (byte)2, (byte)5, (byte)0)));
            Assert.That(U32(p, 10), Is.EqualTo(1_234_567));
            Assert.That(U16(p, 14), Is.EqualTo(11210));
            Assert.That(U32(p, 16), Is.EqualTo(62));
            Assert.That(Encoding.UTF8.GetString(p, 16, p.Length - 17), Is.EqualTo("Crafted Chest"));

            run.GemsLost = true;
            run.End(EnchantReason.CombineFailed);
            p = CraftEnchant165.BuildStatus(run);
            Assert.That((p[0], p[1], p[2], p[5]), Is.EqualTo(((byte)5, (byte)8, (byte)0, (byte)1)));
            Assert.That(CraftEnchant165.BuildEnd(), Is.EqualTo(new byte[] { 0xFF }));
        }
    }
}
