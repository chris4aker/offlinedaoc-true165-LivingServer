using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting assistant economics (QoL, not in 1.65): the mirrored 1.65 rules must match AbstractCraftingSkill exactly,
    /// and the planner must pick the cheapest recipe per target point and walk the expected path.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_CraftAssist165Economics
    {
        // a player whose crafting skills, region, bonus, speed and account are set by the test
        private sealed class SkillPlayer : GamePlayer
        {
            public SkillPlayer() : base(null, null) { }   // never run: made with GetUninitializedObject
            public Dictionary<eCraftingSkill, int> Skills;
            public Region Region;
            public int Bonus;
            public GameClient FakeClient;
            public override int GetCraftingSkillValue(eCraftingSkill skill) => Skills.TryGetValue(skill, out int v) ? v : -1;
            public override Region CurrentRegion { get => Region; }
            public override int CraftingSkillBonus => Bonus;
            public override double CraftingSpeed => 1.0;
            public override GameClient Client => FakeClient;
        }

        private sealed class TestRegion : Region
        {
            public TestRegion() : base(null) { }          // never run: made with GetUninitializedObject
            public bool Capital;
            public override bool IsCapitalCity => Capital;
        }

        // game objects need a GameServer with a database (an empty one) for their static setup
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
        private GameServer _previous;

        private bool _classic;
        private int _sellRatio;
        private int _maxCraftTime;

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _classic = Properties.CLASSIC165_MODE;
            _sellRatio = Properties.ITEM_SELL_RATIO;
            _maxCraftTime = Properties.MAX_CRAFT_TIME;
            Properties.CLASSIC165_MODE = true;
            Properties.ITEM_SELL_RATIO = 50;
            Properties.MAX_CRAFT_TIME = 0;
        }

        [TearDown]
        public void Cleanup()
        {
            GameServer.LoadTestDouble(_previous);
            Properties.CLASSIC165_MODE = _classic;
            Properties.ITEM_SELL_RATIO = _sellRatio;
            Properties.MAX_CRAFT_TIME = _maxCraftTime;
        }

        private static T Skill<T>(eCraftingSkill id) where T : AbstractCraftingSkill
        {
            var skill = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            skill.eSkill = id;
            return skill;
        }

        private static SkillPlayer Player(Dictionary<eCraftingSkill, int> skills, uint privLevel = 1, bool capital = false, int bonus = 0)
        {
            var region = (TestRegion)RuntimeHelpers.GetUninitializedObject(typeof(TestRegion));
            region.Capital = capital;
            var client = (GameClient)RuntimeHelpers.GetUninitializedObject(typeof(GameClient));
            typeof(GameClient).GetField("<Account>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(client, new DbAccount { PrivLevel = privLevel });
            var player = (SkillPlayer)RuntimeHelpers.GetUninitializedObject(typeof(SkillPlayer));
            player.Skills = skills;
            player.Region = region;
            player.Bonus = bonus;
            player.FakeClient = client;
            return player;
        }

        private static DbItemTemplate Material(string name, int model, long price) =>
            new DbItemTemplate { Id_nb = name.Replace(' ', '_'), Name = name, Model = model, Price = price, PackSize = 1 };

        private static Recipe MakeRecipe(eCraftingSkill trade, int level, eObjectType type, long productPrice, params (DbItemTemplate Mat, int Count)[] parts)
        {
            var product = new DbItemTemplate { Id_nb = $"p{trade}{level}{type}", Name = $"product {level}", Object_Type = (int)type,
                Price = productPrice, PackSize = 1, IsDropable = true, IsTradable = true };
            var ingredients = new List<Ingredient>();
            foreach (var p in parts)
                ingredients.Add(new Ingredient(p.Count, p.Mat));
            return new Recipe(product, ingredients, trade, level, true);
        }

        private static readonly DbItemTemplate Cloth = Material("silk cloth squares", 522, 10);
        private static readonly DbItemTemplate Thread = Material("silk heavy thread", 537, 5);
        private static readonly DbItemTemplate Leather = Material("hard leather squares", 521, 20);
        private static readonly DbItemTemplate Bars = Material("alloy metal bars", 519, 30);
        private static readonly DbItemTemplate Boards = Material("ironwood wooden boards", 520, 15);

        private static CraftAssist165Economics.PlanContext Context(eCraftingSkill target, params (CraftAssist165Economics.RecipeInfo Info, long Sell, int Time)[] list)
        {
            var ctx = new CraftAssist165Economics.PlanContext { Target = target, Sell = true };
            foreach (var c in list)
                ctx.Candidates.Add(new CraftAssist165Economics.Candidate { Info = c.Info, SellValue = c.Sell, TimeBeforeCon = c.Time });
            return ctx;
        }

        // ---- the mirrored 1.65 rules

        [Test]
        public void ItemCon_MatchesTheRealOne()
        {
            var tailoring = Skill<Tailoring>(eCraftingSkill.Tailoring);
            for (int skill = 0; skill <= 1100; skill += 7)
                for (int level = 0; level <= 1100; level += 3)
                    Assert.That(CraftAssist165Economics.ItemCon(skill, level), Is.EqualTo(tailoring.GetItemCon(skill, level)));
        }

        [TestCase(false, 0)]
        [TestCase(true, 5)]
        [TestCase(true, 50)]
        [TestCase(true, -20)]
        public void Chances_MatchTheRealOnes(bool capital, int bonus)
        {
            var tailoring = Skill<Tailoring>(eCraftingSkill.Tailoring);
            for (int skill = 100; skill <= 900; skill += 37)
            {
                SkillPlayer player = Player(new() { [eCraftingSkill.Tailoring] = skill }, 1, capital, bonus);
                for (int level = skill - 80; level <= skill + 80; level++)
                {
                    int con = CraftAssist165Economics.ItemCon(skill, level);
                    Assert.That(CraftAssist165Economics.MakeChance(con), Is.EqualTo(tailoring.CalculateChanceToMakeItem(player, level)), $"make {skill}/{level}");
                    Assert.That(CraftAssist165Economics.GainChance(con, capital, bonus), Is.EqualTo(tailoring.CalculateChanceToGainPoint(player, level)), $"gain {skill}/{level}");
                }
            }
        }

        [TestCase(0)]
        [TestCase(7)]
        public void PlayerCraftSeconds_MatchesThePlayerTimer(int maxCraftTime)
        {
            Properties.MAX_CRAFT_TIME = maxCraftTime;
            var tailoring = Skill<Tailoring>(eCraftingSkill.Tailoring);
            foreach (int level in new[] { 15, 99, 100, 315, 715, 915 })
            {
                Recipe recipe = MakeRecipe(eCraftingSkill.Tailoring, level, eObjectType.Cloth, 100, (Cloth, 40), (Thread, 22), (Leather, 150));
                for (int skill = level - 60; skill <= level + 60; skill += 5)
                {
                    SkillPlayer player = Player(new() { [eCraftingSkill.Tailoring] = skill });
                    Assert.That(CraftAssist165Economics.PlayerCraftSeconds(player, recipe), Is.EqualTo(tailoring.GetCraftingTime(player, recipe)), $"{level}/{skill}");
                }
            }
        }

        [Test]
        public void PlayerCraftSeconds_GmStillGetsTheNormalTime()
        {
            var tailoring = Skill<Tailoring>(eCraftingSkill.Tailoring);
            Recipe recipe = MakeRecipe(eCraftingSkill.Tailoring, 715, eObjectType.Cloth, 100, (Cloth, 40), (Thread, 22));
            SkillPlayer player = Player(new() { [eCraftingSkill.Tailoring] = 715 }, 3);
            int normal = tailoring.GetCraftingTime(Player(new() { [eCraftingSkill.Tailoring] = 715 }), recipe);
            Assert.That(tailoring.GetCraftingTime(player, recipe), Is.EqualTo(0));   // a GM's MAKE button: 0 seconds
            // (715 / 100 + 1) * 62 / 4 = 124 seconds for a yellow nightshade robe
            Assert.That(normal, Is.EqualTo(124));
            Assert.That(CraftAssist165Economics.PlayerCraftSeconds(player, recipe, 715), Is.EqualTo(124));
        }

        [Test]
        public void SellValue_IsTheMerchantPrice()
        {
            var robe = new DbItemTemplate { Id_nb = "robe", Name = "robe", Price = 401760, PackSize = 1, IsDropable = true };
            Assert.That(CraftAssist165Economics.SellValue(null, robe), Is.EqualTo(200880));
            var throwing = new DbItemTemplate { Id_nb = "axes", Name = "axes", Price = 3120, PackSize = 20, IsDropable = true };
            Assert.That(CraftAssist165Economics.SellValue(null, throwing), Is.EqualTo(1560));   // the whole pack of 20
            var noDrop = new DbItemTemplate { Id_nb = "nd", Name = "nd", Price = 1000, PackSize = 1, IsDropable = false };
            Assert.That(CraftAssist165Economics.SellValue(null, noDrop), Is.EqualTo(0));
            var ram = new DbItemTemplate { Id_nb = "ram", Name = "ram", Price = 1000, IsDropable = true, Object_Type = (int)eObjectType.SiegeRam };
            Assert.That(CraftAssist165Economics.SellValue(null, ram), Is.EqualTo(0));
            Assert.That(CraftAssist165Economics.AppraiseValue(1, 1, 1, 50, true), Is.EqualTo(1));   // a 1 copper item still sells
        }

        [Test]
        public void MaterialCost_IsPriceTimesCount()
        {
            Recipe recipe = MakeRecipe(eCraftingSkill.Tailoring, 315, eObjectType.Cloth, 100, (Cloth, 40), (Thread, 22));
            Assert.That(CraftAssist165Economics.MaterialCost(recipe), Is.EqualTo(40 * 10 + 22 * 5));
            Assert.That(CraftAssist165Economics.MaterialCost(recipe), Is.EqualTo(recipe.CostToCraft));
        }

        [Test]
        public void PackPricing_WholePacksForOneCraft_AverageForARun()
        {
            Assert.That(CraftAssist165Economics.PackCost(8643, 20, 1), Is.EqualTo(8643));     // one lifebane = a pack of 20
            Assert.That(CraftAssist165Economics.PackCost(8643, 20, 21), Is.EqualTo(2 * 8643));
            Assert.That(CraftAssist165Economics.PackCost(10, 0, 7), Is.EqualTo(70));          // pack size 0 sells singles
            Assert.That(CraftAssist165Economics.PackCost(10, 1, 0), Is.EqualTo(0));
            var poison = new DbItemTemplate { Id_nb = "lifebane", Name = "lifebane", Model = 99, Price = 8643, PackSize = 20 };
            Recipe recipe = MakeRecipe(eCraftingSkill.Alchemy, 300, eObjectType.Poison, 1, (poison, 1), (Material("vial", 99, 10), 2));
            Assert.That(CraftAssist165Economics.MaterialCost(recipe), Is.EqualTo(8643 + 20));
            Assert.That(CraftAssist165Economics.MaterialCostPerCraft(recipe), Is.EqualTo(8643 / 20.0 + 20).Within(1e-9));
        }

        [Test]
        public void SiegeEngines_AreNotPlanned()
        {
            var siege = Skill<SiegeCrafting>(eCraftingSkill.SiegeCrafting);
            var ram = CraftAssist165Economics.Describe(1, MakeRecipe(eCraftingSkill.SiegeCrafting, 100, eObjectType.SiegeRam, 0, (Boards, 300)), siege);
            var ammo = CraftAssist165Economics.Describe(2, MakeRecipe(eCraftingSkill.SiegeCrafting, 100, eObjectType.GenericItem, 0, (Boards, 30)), siege);
            Assert.That(ram.Plannable, Is.False);
            Assert.That(ammo.Plannable, Is.True);
            var ctx = Context(eCraftingSkill.SiegeCrafting, (ram, 0, 10), (ammo, 0, 10));
            int[] skills = CraftAssist165Economics.SkillArray(new Dictionary<eCraftingSkill, int> { [eCraftingSkill.SiegeCrafting] = 100 });
            Assert.That(CraftAssist165Economics.Pick(ctx, skills).RecipeId, Is.EqualTo(2));
        }

        [Test]
        public void UnbuyableMaterials_AreNotPlanned()
        {
            var alchemy = Skill<Alchemy>(eCraftingSkill.Alchemy);
            var drop = CraftAssist165Economics.Describe(1, MakeRecipe(eCraftingSkill.Alchemy, 100, eObjectType.AlchemyTincture, 0,
                (Material("Ancient Troll Blood", 99, 0), 1)), alchemy);
            Assert.That(drop.Buyable, Is.False);
            Assert.That(drop.Plannable, Is.False);
        }

        // ---- what each trade trains and requires (the overrides)

        [Test]
        public void Describe_TailoringTrainsOnePointPerIngredientEntry()
        {
            Recipe recipe = MakeRecipe(eCraftingSkill.Tailoring, 500, eObjectType.Leather, 100, (Leather, 30), (Thread, 10), (Cloth, 2));
            var info = CraftAssist165Economics.Describe(1, recipe, Skill<Tailoring>(eCraftingSkill.Tailoring));
            Assert.That(info.SecondaryGains, Is.EqualTo(new[] { eCraftingSkill.LeatherCrafting, eCraftingSkill.ClothWorking, eCraftingSkill.ClothWorking }));
            Assert.That(info.SecondaryMinimum, Is.EqualTo(470));     // recipe level - 30 (Tailoring.cs)
            Assert.That(info.SecondaryChecks, Is.EquivalentTo(new[] { eCraftingSkill.LeatherCrafting, eCraftingSkill.ClothWorking }));
            Assert.That(info.Trains, Is.EqualTo(CraftAssist165Economics.TrainsCloth | CraftAssist165Economics.TrainsLeather));
        }

        [Test]
        public void Describe_OtherTrades()
        {
            var plate = CraftAssist165Economics.Describe(2, MakeRecipe(eCraftingSkill.ArmorCrafting, 600, eObjectType.Plate, 1, (Bars, 40), (Leather, 4)),
                Skill<ArmorCrafting>(eCraftingSkill.ArmorCrafting));
            Assert.That(plate.SecondaryMinimum, Is.EqualTo(540));    // recipe level - 60
            Assert.That(plate.SecondaryGains, Is.EqualTo(new[] { eCraftingSkill.MetalWorking, eCraftingSkill.LeatherCrafting }));

            var staff = CraftAssist165Economics.Describe(3, MakeRecipe(eCraftingSkill.Fletching, 400, eObjectType.Staff, 1, (Boards, 10)),
                Skill<Fletching>(eCraftingSkill.Fletching));
            Assert.That(staff.SecondaryMinimum, Is.EqualTo(365));    // staff: level - 35
            Assert.That(staff.SecondaryGains, Is.EqualTo(new[] { eCraftingSkill.WoodWorking }));

            var potion = CraftAssist165Economics.Describe(4, MakeRecipe(eCraftingSkill.Alchemy, 300, eObjectType.AlchemyTincture, 1, (Material("bone", 99, 5), 2)),
                Skill<Alchemy>(eCraftingSkill.Alchemy));
            Assert.That(potion.SecondaryGains, Is.EqualTo(new[] { eCraftingSkill.HerbalCrafting }));
            Assert.That(potion.SecondaryMinimum, Is.EqualTo(0));

            var gem = CraftAssist165Economics.Describe(5, MakeRecipe(eCraftingSkill.SpellCrafting, 300, eObjectType.SpellcraftGem, 1, (Material("raw gem", 99, 5), 1)),
                Skill<SpellCrafting>(eCraftingSkill.SpellCrafting));
            Assert.That(gem.SecondaryGains, Is.EqualTo(new[] { eCraftingSkill.GemCutting }));

            // a secondary's own recipe trains only that secondary (ClothWorking.cs), with no secondary minimum
            var doll = CraftAssist165Economics.Describe(6, MakeRecipe(eCraftingSkill.ClothWorking, 300, eObjectType.GenericItem, 1, (Cloth, 2), (Thread, 1)),
                Skill<ClothWorking>(eCraftingSkill.ClothWorking));
            Assert.That(doll.SecondaryGains, Is.Empty);
            Assert.That(doll.SecondaryMinimum, Is.EqualTo(0));
            Assert.That(doll.Trains, Is.EqualTo(CraftAssist165Economics.TrainsCloth));
        }

        [Test]
        public void TradesRaising_SecondariesIncludeTheMainTrades()
        {
            Assert.That(CraftAssist165Economics.TradesRaising(eCraftingSkill.LeatherCrafting), Does.Contain(eCraftingSkill.Tailoring).And.Contain(eCraftingSkill.LeatherCrafting));
            Assert.That(CraftAssist165Economics.TradesRaising(eCraftingSkill.HerbalCrafting), Is.EqualTo(new[] { eCraftingSkill.HerbalCrafting, eCraftingSkill.Alchemy }));
            Assert.That(CraftAssist165Economics.TradesRaising(eCraftingSkill.GemCutting), Is.EqualTo(new[] { eCraftingSkill.GemCutting, eCraftingSkill.SpellCrafting }));
            Assert.That(CraftAssist165Economics.TradesRaising(eCraftingSkill.WeaponCrafting), Is.EqualTo(new[] { eCraftingSkill.WeaponCrafting }));
        }

        [Test]
        public void ApplyGain_StopsSecondariesAt1300()
        {
            var info = CraftAssist165Economics.Describe(1, MakeRecipe(eCraftingSkill.Tailoring, 900, eObjectType.Leather, 1, (Leather, 30), (Cloth, 2), (Thread, 1)),
                Skill<Tailoring>(eCraftingSkill.Tailoring));
            int[] skills = CraftAssist165Economics.SkillArray(new Dictionary<eCraftingSkill, int>
            {
                [eCraftingSkill.Tailoring] = 1300, [eCraftingSkill.LeatherCrafting] = 1300, [eCraftingSkill.ClothWorking] = 1299,
            });
            CraftAssist165Economics.ApplyGain(info, skills);
            Assert.That(skills[(int)eCraftingSkill.Tailoring], Is.EqualTo(1301));        // the main trade has no cap
            Assert.That(skills[(int)eCraftingSkill.LeatherCrafting], Is.EqualTo(1300));
            Assert.That(skills[(int)eCraftingSkill.ClothWorking], Is.EqualTo(1300));     // second cloth entry finds it capped
        }

        // ---- the planner

        [Test]
        public void Pick_LowestNetCostPerPoint()
        {
            var tailoring = Skill<Tailoring>(eCraftingSkill.Tailoring);
            // yellow: 100 copper / 45% = 222 a point; blue: 50 / 30% = 167 a point
            var yellow = CraftAssist165Economics.Describe(1, MakeRecipe(eCraftingSkill.Tailoring, 500, eObjectType.Cloth, 0, (Material("a", 99, 100), 1)), tailoring);
            var blue = CraftAssist165Economics.Describe(2, MakeRecipe(eCraftingSkill.Tailoring, 480, eObjectType.Cloth, 0, (Material("b", 99, 50), 1)), tailoring);
            var ctx = Context(eCraftingSkill.Tailoring, (yellow, 0, 60), (blue, 0, 60));
            int[] skills = CraftAssist165Economics.SkillArray(new Dictionary<eCraftingSkill, int> { [eCraftingSkill.Tailoring] = 500 });
            CraftPlanStep step = CraftAssist165Economics.Pick(ctx, skills);
            Assert.That(step.RecipeId, Is.EqualTo(2));
            Assert.That(step.CostPerPoint, Is.EqualTo(50 / 0.30).Within(1e-6));
            Assert.That(step.CraftSeconds, Is.EqualTo(48));                 // blue: 0.8 of the time
            Assert.That(step.SecondsPerPoint, Is.EqualTo(48 / (1.0 * 0.30)).Within(1e-6));

            // selling the yellow product for 60 makes it 40 / 45% = 89 a point: now it is the cheaper one
            ctx = Context(eCraftingSkill.Tailoring, (yellow, 60, 60), (blue, 0, 60));
            Assert.That(CraftAssist165Economics.Pick(ctx, skills).RecipeId, Is.EqualTo(1));
        }

        [Test]
        public void Pick_TiesGoToFewerSecondsPerPoint()
        {
            var tailoring = Skill<Tailoring>(eCraftingSkill.Tailoring);
            var slow = CraftAssist165Economics.Describe(1, MakeRecipe(eCraftingSkill.Tailoring, 500, eObjectType.Cloth, 0, (Material("a", 99, 90), 1)), tailoring);
            var fast = CraftAssist165Economics.Describe(2, MakeRecipe(eCraftingSkill.Tailoring, 499, eObjectType.Cloth, 0, (Material("b", 99, 90), 1)), tailoring);
            var ctx = Context(eCraftingSkill.Tailoring, (slow, 0, 90), (fast, 0, 30));
            int[] skills = CraftAssist165Economics.SkillArray(new Dictionary<eCraftingSkill, int> { [eCraftingSkill.Tailoring] = 500 });
            Assert.That(CraftAssist165Economics.Pick(ctx, skills).RecipeId, Is.EqualTo(2));
        }

        [Test]
        public void Pick_SkipsGreyPurpleAndUnmetSecondaryMinimums()
        {
            var tailoring = Skill<Tailoring>(eCraftingSkill.Tailoring);
            var grey = CraftAssist165Economics.Describe(1, MakeRecipe(eCraftingSkill.Tailoring, 450, eObjectType.Cloth, 0, (Cloth, 1)), tailoring);
            var purple = CraftAssist165Economics.Describe(2, MakeRecipe(eCraftingSkill.Tailoring, 550, eObjectType.Cloth, 0, (Cloth, 1)), tailoring);
            var needsCloth = CraftAssist165Economics.Describe(3, MakeRecipe(eCraftingSkill.Tailoring, 500, eObjectType.Cloth, 0, (Cloth, 1)), tailoring);
            var ctx = Context(eCraftingSkill.Tailoring, (grey, 0, 10), (purple, 0, 10), (needsCloth, 0, 10));
            var skills = new Dictionary<eCraftingSkill, int> { [eCraftingSkill.Tailoring] = 500, [eCraftingSkill.ClothWorking] = 469 };
            Assert.That(CraftAssist165Economics.Pick(ctx, CraftAssist165Economics.SkillArray(skills)), Is.Null);
            skills[eCraftingSkill.ClothWorking] = 470;   // the minimum is level - 30
            Assert.That(CraftAssist165Economics.Pick(ctx, CraftAssist165Economics.SkillArray(skills)).RecipeId, Is.EqualTo(3));
        }

        [Test]
        public void Pick_SecondaryTargetCountsIngredientEntries()
        {
            var tailoring = Skill<Tailoring>(eCraftingSkill.Tailoring);
            // the same cost; two leather entries give two Leathercrafting points a gain
            var one = CraftAssist165Economics.Describe(1, MakeRecipe(eCraftingSkill.Tailoring, 500, eObjectType.Cloth, 0, (Leather, 5), (Cloth, 5)), tailoring);
            var two = CraftAssist165Economics.Describe(2, MakeRecipe(eCraftingSkill.Tailoring, 500, eObjectType.Cloth, 0, (Leather, 5), (Leather, 5)), tailoring);
            var ctx = Context(eCraftingSkill.LeatherCrafting, (one, 0, 10), (two, 0, 10));
            var skills = new Dictionary<eCraftingSkill, int>
                { [eCraftingSkill.Tailoring] = 500, [eCraftingSkill.LeatherCrafting] = 600, [eCraftingSkill.ClothWorking] = 600 };
            CraftPlanStep step = CraftAssist165Economics.Pick(ctx, CraftAssist165Economics.SkillArray(skills));
            Assert.That(step.RecipeId, Is.EqualTo(2));
            Assert.That(step.PointsPerGain, Is.EqualTo(2));
            Assert.That(step.CostPerPoint, Is.EqualTo(200 / 0.45 / 2).Within(1e-6));

            skills[eCraftingSkill.LeatherCrafting] = 1299;   // only one point left below the cap
            Assert.That(CraftAssist165Economics.Pick(ctx, CraftAssist165Economics.SkillArray(skills)).PointsPerGain, Is.EqualTo(1));
            skills[eCraftingSkill.LeatherCrafting] = 1300;
            Assert.That(CraftAssist165Economics.Pick(ctx, CraftAssist165Economics.SkillArray(skills)), Is.Null);
        }

        [Test]
        public void Simulate_SumsTheExpectedPath()
        {
            var cloth = Skill<ClothWorking>(eCraftingSkill.ClothWorking);
            var doll = CraftAssist165Economics.Describe(7, MakeRecipe(eCraftingSkill.ClothWorking, 100, eObjectType.GenericItem, 0, (Material("x", 99, 10), 1)), cloth);
            var ctx = Context(eCraftingSkill.ClothWorking, (doll, 4, 20));
            var est = CraftAssist165Economics.Simulate(ctx, new Dictionary<eCraftingSkill, int> { [eCraftingSkill.ClothWorking] = 100 }, 103);

            // three yellow gains: each 1 / (0.92 * 0.45) attempts, materials 10 / 0.45, resale 4 / 0.45
            double attempts = 1 / (0.92 * 0.45);
            Assert.That(est.Feasible, Is.True);
            Assert.That(est.Reached, Is.EqualTo(103));
            Assert.That(est.Crafts, Is.EqualTo(3 * attempts).Within(1e-9));
            Assert.That(est.Seconds, Is.EqualTo(3 * attempts * 20).Within(1e-9));
            Assert.That(est.MaterialCost, Is.EqualTo(3 * 10 / 0.45).Within(1e-9));
            Assert.That(est.Resale, Is.EqualTo(3 * 4 / 0.45).Within(1e-9));
            Assert.That(est.NetCost, Is.EqualTo(3 * 6 / 0.45).Within(1e-9));
            Assert.That(est.FirstRecipeId, Is.EqualTo(7));
            Assert.That(est.Skills, Has.Count.EqualTo(1));
            Assert.That(est.Skills[0], Is.EqualTo((eCraftingSkill.ClothWorking, 100, 103, est.Seconds)));
        }

        [Test]
        public void Simulate_ProjectsTheSecondariesAndUnlocksRequirements()
        {
            var tailoring = Skill<Tailoring>(eCraftingSkill.Tailoring);
            // the robe needs Clothworking 470 (level 500 - 30); the cap needs none and trains Clothworking twice a gain
            var robe = CraftAssist165Economics.Describe(1, MakeRecipe(eCraftingSkill.Tailoring, 500, eObjectType.Cloth, 0, (Cloth, 1)), tailoring);
            var cap = CraftAssist165Economics.Describe(2, MakeRecipe(eCraftingSkill.Tailoring, 480, eObjectType.GenericItem, 0, (Cloth, 50), (Thread, 50)), tailoring);
            var ctx = Context(eCraftingSkill.Tailoring, (robe, 0, 10), (cap, 0, 10));
            var est = CraftAssist165Economics.Simulate(ctx,
                new Dictionary<eCraftingSkill, int> { [eCraftingSkill.Tailoring] = 490, [eCraftingSkill.ClothWorking] = 466 }, 500);

            Assert.That(est.Feasible, Is.True);
            Assert.That(est.FirstRecipeId, Is.EqualTo(2));               // the robe is locked at Clothworking 466
            Assert.That(est.Skills[0].Skill, Is.EqualTo(eCraftingSkill.Tailoring));
            Assert.That(est.Skills[0].Projected, Is.EqualTo(500));
            var clothRow = est.Skills.Find(r => r.Skill == eCraftingSkill.ClothWorking);
            Assert.That(clothRow.Now, Is.EqualTo(466));
            Assert.That(clothRow.Projected, Is.GreaterThanOrEqualTo(470));
            Assert.That(clothRow.EtaSeconds, Is.LessThanOrEqualTo(est.Seconds));
        }

        [Test]
        public void Simulate_NothingCanRaiseIt()
        {
            var tailoring = Skill<Tailoring>(eCraftingSkill.Tailoring);
            var low = CraftAssist165Economics.Describe(1, MakeRecipe(eCraftingSkill.Tailoring, 460, eObjectType.GenericItem, 0, (Material("a", 99, 1), 1)), tailoring);
            var ctx = Context(eCraftingSkill.Tailoring, (low, 0, 10));
            // it goes grey at 510 (level - skill <= -50), so 520 cannot be reached
            var est = CraftAssist165Economics.Simulate(ctx, new Dictionary<eCraftingSkill, int> { [eCraftingSkill.Tailoring] = 500 }, 520);
            Assert.That(est.Feasible, Is.False);
            Assert.That(est.Reason, Is.EqualTo(5));
            Assert.That(est.Reached, Is.EqualTo(510));

            est = CraftAssist165Economics.Simulate(Context(eCraftingSkill.Tailoring), new Dictionary<eCraftingSkill, int> { [eCraftingSkill.Tailoring] = 500 }, 520);
            Assert.That(est.Feasible, Is.False);
            Assert.That(est.FirstRecipe, Is.Null);
        }

        [Test]
        public void Simulate_GoalAlreadyReached()
        {
            var est = CraftAssist165Economics.Simulate(Context(eCraftingSkill.Tailoring), new Dictionary<eCraftingSkill, int> { [eCraftingSkill.Tailoring] = 700 }, 650);
            Assert.That(est.Feasible, Is.True);
            Assert.That(est.Reason, Is.EqualTo(1));
            Assert.That(est.Crafts, Is.EqualTo(0));
        }

        [Test]
        public void Simulate_IsFastOnALongPath()
        {
            var tailoring = Skill<Tailoring>(eCraftingSkill.Tailoring);
            var list = new List<(CraftAssist165Economics.RecipeInfo, long, int)>();
            ushort id = 1;
            for (int level = 15; level <= 1015; level += 2)       // 500 recipes, every 2 levels
                for (int k = 0; k < 3; k++)
                    list.Add((CraftAssist165Economics.Describe(id++, MakeRecipe(eCraftingSkill.Tailoring, level, eObjectType.Cloth, 0,
                        (Material("c" + k, 522, 10 + level + k), 4), (Material("t" + k, 537, 5), 2)), tailoring), 0, 30));
            var ctx = Context(eCraftingSkill.Tailoring, list.ToArray());
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var est = CraftAssist165Economics.Simulate(ctx,
                new Dictionary<eCraftingSkill, int> { [eCraftingSkill.Tailoring] = 1, [eCraftingSkill.ClothWorking] = 1 }, 1000);
            watch.Stop();
            Assert.That(est.Feasible, Is.True);
            Assert.That(est.Reached, Is.EqualTo(1000));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(500));
        }

        // ---- op 42

        [Test]
        public void RecipeRow_ByteLayout()
        {
            var info = new CraftAssist165Economics.RecipeInfo
            {
                Id = 4908, Level = 715, MaterialCost = 387504, SecondaryMinimum = 685,
                Trains = CraftAssist165Economics.TrainsCloth,
            };
            var row = new byte[18];
            CraftAssist165Economics.WriteRecipeRow(row, 0, info, 200880, 3, 139);
            Assert.That(row, Is.EqualTo(new byte[]
            {
                0x2C, 0x13,                 // 4908
                0xCB, 0x02,                 // 715
                0xB0, 0xE9, 0x05, 0x00,     // 387504
                0xB0, 0x10, 0x03, 0x00,     // 200880
                0xAD, 0x02,                 // 685
                0x01,                       // trains Clothworking
                0x03,                       // requirement met, materials in the backpack
                0x8B, 0x00,                 // 139 s
            }));
        }
    }
}
