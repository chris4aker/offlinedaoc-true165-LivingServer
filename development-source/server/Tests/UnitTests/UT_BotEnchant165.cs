using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafter gamebots' enchanting (owner 2026-11-08): the gems and tinctures a bot puts on its crafted gear follow the real
    /// spellcraft / alchemy formulas (imbue max, spellcrafter cap, no overcharge, charges by quality, combine rules), the
    /// enchanters' skill (recipe con) and the crafter's budget.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotEnchant165
    {
        // the real SpellCrafting formulas, to compare against
        private sealed class RealSpell : SpellCrafting
        {
            public int Max(DbInventoryItem item) => GetItemMaxImbuePoints(item);
            public int Gem(int type, int value) => GetGemImbuePoints(type, value);
        }

        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
        private GameServer _previous;

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void Cleanup() => GameServer.LoadTestDouble(_previous);

        private static DbInventoryItem Item(eObjectType type, int level, int quality, bool crafted = true)
        {
            var template = new DbItemTemplate
            {
                Id_nb = $"test_{type}_{level}_{quality}",
                Name = $"test {type}",
                Object_Type = (int)type,
                Item_Type = GlobalConstants.IsArmor((int)type) ? (int)eInventorySlot.TorsoArmor : (int)eInventorySlot.RightHandWeapon,
                Level = level,
                Quality = quality,
                MaxCount = 1,
                PackSize = 1,
            };
            var item = new GameInventoryItem(template) { IsCrafted = crafted };
            return item;
        }

        // the 1.65 gem ladder: ten tiers 100 recipe levels apart (stats 1..28, HP 4..76, resists 1..17)
        private static Dictionary<int, BotEnchant165.GemOption[]> Gems()
        {
            var gems = new Dictionary<int, BotEnchant165.GemOption[]>();
            ushort id = 1;
            void Ladder(int type, int start, int[] values)
            {
                gems[type] = values.Select((v, i) => new BotEnchant165.GemOption(id++, $"gem {type} {v}", type, v, start + 100 * i, 100 + 1000L * i)).ToArray();
            }
            int[] stats = [1, 4, 7, 10, 13, 16, 19, 22, 25, 28];
            foreach (int stat in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
                Ladder(stat, 1 + stat * 3, stats);
            Ladder((int)eProperty.MaxHealth, 64, [4, 12, 20, 28, 36, 44, 52, 60, 68, 76]);
            foreach (int resist in Enumerable.Range((int)eProperty.Resist_First, 9))
                Ladder(resist, 92, [1, 2, 3, 5, 7, 9, 11, 13, 15, 17]);
            return gems;
        }

        private static List<BotEnchant165.TinctureOption> Tinctures() =>
        [
            new(1, "volatile fire alloy weapon tincture", 1, 92100, 10, 0, 20, 594, 5000),
            new(2, "volatile fire asterite weapon tincture", 1, 92116, 10, 0, 40, 839, 9000),
            new(3, "stable fire alloy tincture", 0, 0, 0, 92100, 20, 534, 4000),
            new(4, "reactive fire asterite armor tincture", 0, 92116, 10, 0, 40, 969, 9000),
            new(5, "stable fire asterite tincture", 0, 0, 0, 92116, 40, 814, 8000),
        ];

        private static readonly int[] Armour = [1, 3, 2, 4];

        [Test]
        public void Formulas_AreTheRealSpellcraftOnes()
        {
            var real = (RealSpell)RuntimeHelpers.GetUninitializedObject(typeof(RealSpell));
            foreach (int type in Enumerable.Range(1, 165))
                for (int value = 1; value <= 80; value++)
                    Assert.That(BotEnchant165.GemPoints(type, value), Is.EqualTo(real.Gem(type, value)), $"type {type} value {value}");
            for (int level = 1; level <= 51; level++)
                for (int quality = 85; quality <= 100; quality++)
                {
                    DbInventoryItem item = Item(eObjectType.Plate, level, quality);
                    Assert.That(BotEnchant165.ItemMaxImbue(item), Is.EqualTo(real.Max(item)));
                }
            Assert.That(BotEnchant165.SpellcrafterCap(1000), Is.EqualTo(50));
            Assert.That(BotEnchant165.SpellcrafterCap(319), Is.EqualTo(15));
        }

        [Test]
        public void TotalImbue_SumsPlusTheBiggestHalved()
        {
            DbInventoryItem item = Item(eObjectType.Chain, 40, 99);
            // four stats of 7: 5 points each -> (20 + 5) / 2 = 12
            Assert.That(BotEnchant165.TotalImbue(item, [(1, 7), (2, 7), (3, 7), (4, 7)]), Is.EqualTo(12));
            // with an existing bonus on the item (HP 20 = 5 points) and a resist 5 (8 points): (5 + 8 + 8) / 2 = 10
            item.Bonus1Type = (int)eProperty.MaxHealth;
            item.Bonus1 = 20;
            Assert.That(BotEnchant165.TotalImbue(item, [((int)eProperty.Resist_Slash, 5)]), Is.EqualTo(10));
        }

        [Test]
        public void PickGems_StaysWithinTheItemMaxAndTheSpellcrafterCap_NoOvercharge()
        {
            var gems = Gems();
            foreach ((int level, int quality, int skill) in new[] { (50, 100, 1100), (50, 94, 1100), (30, 97, 1100), (50, 100, 319), (20, 96, 150) })
            {
                DbInventoryItem item = Item(eObjectType.Plate, level, quality);
                List<BotEnchant165.GemOption> picked = BotEnchant165.PickGems(item, Armour, gems, skill, long.MaxValue / 4);
                int limit = System.Math.Min(BotEnchant165.ItemMaxImbue(item), skill / 20);
                int total = BotEnchant165.TotalImbue(item, picked.Select(g => (g.BonusType, g.BonusValue)));
                Assert.That(total, Is.LessThanOrEqualTo(limit), $"level {level} q{quality} skill {skill}");
                Assert.That(picked.Count, Is.LessThanOrEqualTo(4));
                Assert.That(picked.Select(g => g.BonusType).Distinct().Count(), Is.EqualTo(picked.Count), "one gem per bonus type");
                Assert.That(picked.All(g => g.Level - skill <= 49), Is.True, "only recipes the spellcrafter can make");
                Assert.That(picked, Is.Not.Empty);
                // nothing could be raised one more tier and still fit (the points are used)
                foreach (BotEnchant165.GemOption gem in picked)
                {
                    BotEnchant165.GemOption next = gems[gem.BonusType].FirstOrDefault(g => g.BonusValue > gem.BonusValue && g.Level - skill <= 49);
                    if (next == null)
                        continue;
                    var raised = picked.Select(g => g == gem ? next : g).Select(g => (g.BonusType, g.BonusValue));
                    Assert.That(BotEnchant165.TotalImbue(item, raised), Is.GreaterThan(limit), $"{gem.Name} could go higher");
                }
            }
        }

        [Test]
        public void PickGems_SpreadsTheTiersEvenly()
        {
            DbInventoryItem item = Item(eObjectType.Plate, 50, 100);   // 31 points
            List<BotEnchant165.GemOption> picked = BotEnchant165.PickGems(item, Armour, Gems(), 1100, long.MaxValue / 4);
            Assert.That(picked.Count, Is.EqualTo(4));
            int[] values = picked.Select(g => g.BonusValue).ToArray();
            Assert.That(values.Max() - values.Min(), Is.LessThanOrEqualTo(3), "tiers at most one apart");
            Assert.That(picked[0].BonusType, Is.EqualTo(1), "best bonus first");
        }

        [Test]
        public void PickGems_RespectsTheBudgetFreeSlotsAndExistingBonuses()
        {
            var gems = Gems();
            DbInventoryItem item = Item(eObjectType.Leather, 45, 99);
            List<BotEnchant165.GemOption> cheap = BotEnchant165.PickGems(item, Armour, gems, 1100, 2_500);
            long cost = cheap.Sum(g => g.Cost);
            Assert.That(cost + BotEnchant165.Fee(cost), Is.LessThanOrEqualTo(2_500));
            Assert.That(BotEnchant165.PickGems(item, Armour, gems, 1100, 50), Is.Empty, "no money, no gems");

            item.Bonus1Type = 1;
            item.Bonus1 = 10;
            item.Bonus2Type = 3;
            item.Bonus2 = 10;
            List<BotEnchant165.GemOption> rest = BotEnchant165.PickGems(item, Armour, gems, 1100, long.MaxValue / 4);
            Assert.That(rest.Count, Is.LessThanOrEqualTo(2));
            Assert.That(rest.Select(g => g.BonusType), Has.No.Member(1));
            Assert.That(rest.Select(g => g.BonusType), Has.No.Member(3));

            item.Bonus3Type = 2;
            item.Bonus3 = 1;
            item.Bonus4Type = 4;
            item.Bonus4 = 1;
            Assert.That(BotEnchant165.PickGems(item, Armour, gems, 1100, long.MaxValue / 4), Is.Empty, "a full item takes no gems");
            Assert.That(BotEnchant165.PickGems(Item(eObjectType.Plate, 50, 100), Armour, gems, 19, long.MaxValue / 4), Is.Empty,
                "skill 19: cap 0");
        }

        [Test]
        public void PickTincture_FollowsTheCombineRulesSkillAndLevel()
        {
            var tinctures = Tinctures();
            // a weapon takes a weapon proc, the best its level allows
            BotEnchant165.TinctureOption t = BotEnchant165.PickTincture(Item(eObjectType.Sword, 45, 99), tinctures, 1000, long.MaxValue / 4, 0);
            Assert.That(t.RecipeId, Is.EqualTo(2));
            Assert.That(t.Kind, Is.EqualTo(BotEnchant165.TinctureKind.WeaponProc));
            // level 30: the level 40 proc would raise its level requirement, so the level 20 one
            Assert.That(BotEnchant165.PickTincture(Item(eObjectType.Sword, 30, 99), tinctures, 1000, long.MaxValue / 4, 0).RecipeId, Is.EqualTo(1));
            // armour: an armour proc beats charges; never a weapon tincture
            Assert.That(BotEnchant165.PickTincture(Item(eObjectType.Plate, 45, 99), tinctures, 1100, long.MaxValue / 4, 0).RecipeId, Is.EqualTo(4));
            // the alchemist cannot make the armour proc (969 - 700 > 49): the best charges it can make
            t = BotEnchant165.PickTincture(Item(eObjectType.Plate, 45, 99), tinctures, 700, long.MaxValue / 4, 0);
            Assert.That(t.Kind, Is.EqualTo(BotEnchant165.TinctureKind.Charges));
            Assert.That(t.RecipeId, Is.EqualTo(3));
            // instruments take only type-0 tinctures; jewellery none; an item with a proc none; no skill / money none
            Assert.That(BotEnchant165.PickTincture(Item(eObjectType.Instrument, 45, 99), tinctures, 1100, long.MaxValue / 4, 0).TypeDamage, Is.EqualTo(0));
            Assert.That(BotEnchant165.PickTincture(Item(eObjectType.Magical, 45, 99), tinctures, 1100, long.MaxValue / 4, 0), Is.Null);
            DbInventoryItem procced = Item(eObjectType.Sword, 45, 99);
            procced.ProcSpellID = 1;
            Assert.That(BotEnchant165.PickTincture(procced, tinctures, 1100, long.MaxValue / 4, 0), Is.Null);
            Assert.That(BotEnchant165.PickTincture(Item(eObjectType.Sword, 45, 99), tinctures, 400, long.MaxValue / 4, 0), Is.Null);
            Assert.That(BotEnchant165.PickTincture(Item(eObjectType.Sword, 45, 99), tinctures, 1100, 5_000, 0), Is.Null, "5000 + 10% fee");
        }

        [Test]
        public void ApplyTincture_IsAlchemysEffect_ChargesByQuality()
        {
            var charges = new BotEnchant165.TinctureOption(3, "stable", 0, 0, 0, 92100, 20, 534, 4000);
            foreach ((int quality, int expected) in new[] { (90, 2), (93, 2), (94, 2), (97, 5), (99, 7), (100, 10) })
            {
                DbInventoryItem item = Item(eObjectType.Plate, 30, quality);
                BotEnchant165.ApplyTincture(item, charges);
                Assert.That(item.MaxCharges, Is.EqualTo(expected), $"quality {quality}");
                Assert.That(item.Charges, Is.EqualTo(expected));
                Assert.That(item.SpellID, Is.EqualTo(92100));
                Assert.That(item.ProcSpellID, Is.EqualTo(0));
                Assert.That(item.LevelRequirement, Is.EqualTo(20));
            }
            DbInventoryItem weapon = Item(eObjectType.Sword, 45, 99);
            BotEnchant165.ApplyTincture(weapon, Tinctures()[1]);
            Assert.That(weapon.ProcSpellID, Is.EqualTo(92116));
            Assert.That(weapon.ProcChance, Is.EqualTo(10));
            Assert.That(weapon.SpellID, Is.EqualTo(0));
            Assert.That(weapon.LevelRequirement, Is.EqualTo(40));
        }

        [Test]
        public void ApplyGems_FillsTheEmptySlotsInOrder()
        {
            DbInventoryItem item = Item(eObjectType.Chain, 40, 99);
            item.Bonus1Type = (int)eProperty.MaxHealth;
            item.Bonus1 = 12;
            BotEnchant165.ApplyGems(item, [new(1, "a", 1, 7, 218, 1), new(2, "b", 3, 4, 121, 1)]);
            Assert.That((item.Bonus1Type, item.Bonus1), Is.EqualTo(((int)eProperty.MaxHealth, 12)));
            Assert.That((item.Bonus2Type, item.Bonus2), Is.EqualTo((1, 7)));
            Assert.That((item.Bonus3Type, item.Bonus3), Is.EqualTo((3, 4)));
            Assert.That(item.Bonus4Type, Is.EqualTo(0));
            Assert.That(BotEnchant165.FreeGemSlots(item), Is.EqualTo(1));
        }

        [Test]
        public void WorthEnchanting_ChanceFeeAndBonuses()
        {
            Assert.That(BotEnchant165.IsWorthEnchanting(Item(eObjectType.Plate, 15, 94)), Is.True);
            Assert.That(BotEnchant165.IsWorthEnchanting(Item(eObjectType.Plate, 14, 99)), Is.False, "below level 15");
            Assert.That(BotEnchant165.IsWorthEnchanting(Item(eObjectType.Plate, 30, 93)), Is.False, "below 94%");
            Assert.That(BotEnchant165.IsWorthEnchanting(Item(eObjectType.Plate, 30, 99, crafted: false)), Is.False, "not crafted");
            Assert.That(BotEnchant165.IsWorthEnchanting(Item(eObjectType.Magical, 30, 99)), Is.False, "jewellery");
            Assert.That(BotEnchant165.IsWorthEnchanting(Item(eObjectType.Instrument, 30, 99)), Is.True);

            Assert.That(BotEnchant165.EnchantChance(0), Is.EqualTo(15));
            Assert.That(BotEnchant165.EnchantChance(50 * 10_000), Is.EqualTo(25));
            Assert.That(BotEnchant165.EnchantChance(long.MaxValue), Is.EqualTo(75));
            Assert.That(BotEnchant165.Fee(10_000), Is.EqualTo(1_000));
            Assert.That(BotEnchant165.Fee(5), Is.EqualTo(1));
            Assert.That(BotEnchant165.Fee(0), Is.EqualTo(0));

            for (int variant = 0; variant < 6; variant++)
            {
                int[] cloth = BotEnchant165.WantedBonuses(Item(eObjectType.Cloth, 30, 99), eRealm.Midgard, variant);
                Assert.That(cloth[0], Is.EqualTo((int)eProperty.Piety));
                Assert.That(cloth.Distinct().Count(), Is.EqualTo(4));
                Assert.That(BotEnchant165.WantedBonuses(Item(eObjectType.Cloth, 30, 99), eRealm.Albion, variant)[0], Is.EqualTo((int)eProperty.Intelligence));
                Assert.That(BotEnchant165.WantedBonuses(Item(eObjectType.Sword, 30, 99), eRealm.Albion, variant)[0], Is.EqualTo((int)eProperty.Strength));
                Assert.That(BotEnchant165.WantedBonuses(Item(eObjectType.Longbow, 30, 99), eRealm.Albion, variant)[0], Is.EqualTo((int)eProperty.Dexterity));
            }
        }
    }
}
