using System;
using System.Collections.Generic;
using DOL.Database;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Crafting assistant run logic (owner 2026-11-08): state rules, stop reasons, buy plan, sell value and the
    /// op 40 / 41 byte layout from the crafting overhaul spec.</summary>
    [TestFixture]
    public class UT_CraftAssist165
    {
        private static ushort U16(byte[] p, int body) => BitConverter.ToUInt16(p, body - 4);
        private static uint U32(byte[] p, int body) => BitConverter.ToUInt32(p, body - 4);
        private static int I32(byte[] p, int body) => BitConverter.ToInt32(p, body - 4);

        private static CraftAssistRun RunningRun()
        {
            var run = new CraftAssistRun();
            run.Begin(eCraftingSkill.Tailoring, 750, buy: true, sell: true, now: 734, nowMs: 10_000);
            return run;
        }

        [Test]
        public void Begin_ResetsTheNumbersAndRuns()
        {
            var run = RunningRun();
            run.Crafts = 3; run.Spent = 50; run.Earned = 20; run.Now = 736;
            run.End(CraftAssistReason.StoppedByPlayer, 20_000);
            run.Begin(eCraftingSkill.ClothWorking, 800, false, true, 788, 30_000);
            Assert.That(run.State, Is.EqualTo(CraftAssistState.Running));
            Assert.That(run.Reason, Is.EqualTo(CraftAssistReason.None));
            Assert.That((run.Crafts, run.Spent, run.Earned, run.PointsGained), Is.EqualTo((0, 0L, 0L, 0)));
            Assert.That(run.Target, Is.EqualTo(eCraftingSkill.ClothWorking));
        }

        [Test]
        public void End_GoalReachedFinishes_OtherReasonsStop_OnlyOnce()
        {
            var run = RunningRun();
            Assert.That(run.End(CraftAssistReason.GoalReached, 70_000), Is.True);
            Assert.That(run.State, Is.EqualTo(CraftAssistState.Finished));
            Assert.That(run.End(CraftAssistReason.Interrupted, 80_000), Is.False, "a second stop is ignored");
            Assert.That(run.Reason, Is.EqualTo(CraftAssistReason.GoalReached));

            run = RunningRun();
            run.End(CraftAssistReason.OutOfMoney, 70_000);
            Assert.That(run.State, Is.EqualTo(CraftAssistState.Stopped));
            Assert.That(run.Reason, Is.EqualTo(CraftAssistReason.OutOfMoney));
        }

        [Test]
        public void Elapsed_RunsUntilTheStop()
        {
            var run = RunningRun();
            Assert.That(run.ElapsedSeconds(25_500), Is.EqualTo(15));
            run.End(CraftAssistReason.StoppedByPlayer, 40_000);
            Assert.That(run.ElapsedSeconds(99_000), Is.EqualTo(30));
            Assert.That(new CraftAssistRun().ElapsedSeconds(99_000), Is.EqualTo(0));
        }

        [TestCase(CraftAssistOutcome.Made, CraftAssistReason.None)]
        [TestCase(CraftAssistOutcome.Failed, CraftAssistReason.None)]
        [TestCase(CraftAssistOutcome.Interrupted, CraftAssistReason.Interrupted)]
        [TestCase(CraftAssistOutcome.None, CraftAssistReason.Interrupted)]
        [TestCase(CraftAssistOutcome.StoppedByPlayer, CraftAssistReason.StoppedByPlayer)]
        [TestCase(CraftAssistOutcome.MaterialsGone, CraftAssistReason.Other)]
        public void Outcome_MapsToStopReason(CraftAssistOutcome outcome, CraftAssistReason reason) =>
            Assert.That(CraftAssist165.ReasonFor(outcome), Is.EqualTo(reason));

        [Test]
        public void Status_ByteLayout()
        {
            var run = RunningRun();
            run.Now = 740; run.Crafts = 12; run.Spent = 123_456; run.Earned = 7_890; run.RecipeId = 30_001;
            run.Baseline = 200_000; run.Current = -5; run.SecondsLeft = 321.4;
            run.Rows = new()
            {
                new((byte)eCraftingSkill.Tailoring, 740, 750, 321.4),
                new((byte)eCraftingSkill.LeatherCrafting, 675, 680, 100),
            };
            byte[] p = CraftAssist165.BuildStatus(run, 70_000);

            Assert.That(p.Length, Is.EqualTo(43 - 4 + 2 * 9));
            Assert.That(p[0], Is.EqualTo(1), "state running");
            Assert.That(p[1], Is.EqualTo(0), "reason");
            Assert.That(p[2], Is.EqualTo(11), "target");
            Assert.That(p[3], Is.EqualTo(3), "buy + sell flags");
            Assert.That(U16(p, 8), Is.EqualTo(750));
            Assert.That(U16(p, 10), Is.EqualTo(740));
            Assert.That(U16(p, 12), Is.EqualTo(12));
            Assert.That(U16(p, 14), Is.EqualTo(6), "points gained");
            Assert.That(U32(p, 16), Is.EqualTo(123_456u));
            Assert.That(U32(p, 20), Is.EqualTo(7_890u));
            Assert.That(U16(p, 24), Is.EqualTo(30_001));
            Assert.That(I32(p, 26), Is.EqualTo(200_000));
            Assert.That(I32(p, 30), Is.EqualTo(-5), "current estimate is signed");
            Assert.That(U32(p, 34), Is.EqualTo(321u));
            Assert.That(U32(p, 38), Is.EqualTo(60u), "elapsed");
            Assert.That(p[42 - 4], Is.EqualTo(2));
            Assert.That(p[43 - 4], Is.EqualTo(11));
            Assert.That(U16(p, 44), Is.EqualTo(740));
            Assert.That(U16(p, 46), Is.EqualTo(750));
            Assert.That(U32(p, 48), Is.EqualTo(321u));
            Assert.That(p[52 - 4], Is.EqualTo(7));
            Assert.That(U16(p, 53), Is.EqualTo(675));
            Assert.That(U16(p, 55), Is.EqualTo(680));
            Assert.That(U32(p, 57), Is.EqualTo(100u));
        }

        [Test]
        public void Status_ClampsAndCapsRowsAtEight_FitsTheHPacket()
        {
            var run = RunningRun();
            run.Crafts = 100_000; run.Spent = -3; run.Baseline = long.MaxValue;
            for (int i = 0; i < 12; i++)
                run.Rows.Add(new((byte)i, 70_000, 1, -9));
            byte[] p = CraftAssist165.BuildStatus(run, 10_000);
            Assert.That(p[42 - 4], Is.EqualTo(8));
            Assert.That(p.Length, Is.EqualTo(39 + 72));
            Assert.That(p.Length, Is.LessThanOrEqualTo(123), "Hire165.Send carries body [4..126]");
            Assert.That(U16(p, 12), Is.EqualTo(ushort.MaxValue));
            Assert.That(U32(p, 16), Is.EqualTo(0u));
            Assert.That(I32(p, 26), Is.EqualTo(int.MaxValue));
            Assert.That(U16(p, 44), Is.EqualTo(ushort.MaxValue));
            Assert.That(U32(p, 48), Is.EqualTo(0u), "negative eta reads 0");
        }

        [Test]
        public void Status_IdleRunIsAllZero()
        {
            byte[] p = CraftAssist165.BuildStatus(new CraftAssistRun(), 5_000);
            Assert.That(p.Length, Is.EqualTo(39));
            Assert.That(p, Is.All.EqualTo(0));
        }

        [Test]
        public void Estimate_ByteLayout()
        {
            var est = new CraftEstimate
            {
                Feasible = true, From = 734, Goal = 750, Crafts = 40.6, Seconds = 812.2, MaterialCost = 50_000.4,
                Resale = 60_000, NetCost = -9_999.6, Skills = new() { (eCraftingSkill.Tailoring, 734, 750, 812.2) },
            };
            byte[] p = CraftAssist165.BuildEstimate(11, est, 1, 2, 1_234_567, 4_321);
            Assert.That(p.Length, Is.EqualTo(39 - 4 + 9));
            Assert.That((p[0], p[1], p[2], p[3]), Is.EqualTo(((byte)11, (byte)1, (byte)0, (byte)0)));
            Assert.That(U16(p, 8), Is.EqualTo(734), "from the estimate");
            Assert.That(U16(p, 10), Is.EqualTo(750));
            Assert.That(U32(p, 12), Is.EqualTo(41u));
            Assert.That(U32(p, 16), Is.EqualTo(812u));
            Assert.That(U32(p, 20), Is.EqualTo(50_000u));
            Assert.That(U32(p, 24), Is.EqualTo(60_000u));
            Assert.That(I32(p, 28), Is.EqualTo(-10_000));
            Assert.That(U32(p, 32), Is.EqualTo(1_234_567u));
            Assert.That(U16(p, 36), Is.EqualTo(4_321));
            Assert.That(p[38 - 4], Is.EqualTo(1));
            Assert.That(p[39 - 4], Is.EqualTo(11));
            Assert.That(U16(p, 40), Is.EqualTo(734));
            Assert.That(U16(p, 42), Is.EqualTo(750));
            Assert.That(U32(p, 44), Is.EqualTo(812u));
        }

        [Test]
        public void Estimate_NotFeasibleCarriesTheReason_NullIsOther()
        {
            byte[] p = CraftAssist165.BuildEstimate(7, new CraftEstimate { Feasible = false, Reason = 5 }, 675, 700, 10, 0);
            Assert.That((p[1], p[2]), Is.EqualTo(((byte)0, (byte)5)));
            Assert.That(U16(p, 8), Is.EqualTo(675), "falls back to the player's value");
            Assert.That(U16(p, 10), Is.EqualTo(700));

            p = CraftAssist165.BuildEstimate(7, null, 675, 700, 10, 0);
            Assert.That((p[1], p[2]), Is.EqualTo(((byte)0, (byte)CraftAssistReason.Other)));
            Assert.That(p.Length, Is.EqualTo(35));
        }

        [Test]
        public void PlanBuy_WholePacksAtTemplatePrice()
        {
            var square = new DbItemTemplate { Name = "cloth square", Price = 30, PackSize = 1 };
            var poison = new DbItemTemplate { Name = "lifebane", Price = 8_643, PackSize = 20 };
            var none = new DbItemTemplate { Name = "thread", Price = 5, PackSize = 0 };
            List<CraftAssistBuyLine> lines = CraftAssist165.PlanBuy(new[] { (square, 4), (poison, 3), (none, 2), (square, 0) });
            Assert.That(lines.Count, Is.EqualTo(3), "nothing bought for a need of 0");
            Assert.That((lines[0].Packs, lines[0].Amount, lines[0].Cost), Is.EqualTo((4, 4, 120L)));
            Assert.That((lines[1].Packs, lines[1].Amount, lines[1].Cost), Is.EqualTo((1, 20, 8_643L)), "one pack of 20");
            Assert.That((lines[2].Packs, lines[2].Amount, lines[2].Cost), Is.EqualTo((2, 2, 10L)), "pack size 0 = single pieces");
        }

        [TestCase(1000, 1, 1, 50, true, 500)]
        [TestCase(52, 20, 20, 50, true, 26)]      // a pack of 20 poisons sells as one pack
        [TestCase(1, 1, 1, 50, true, 1)]          // a 1-copper item still sells for 1
        [TestCase(1000, 1, 1, 50, false, 0)]      // not sellable: kept
        [TestCase(0, 1, 1, 50, true, 0)]          // siege engines and other price-0 products
        public void MerchantSellValue_MatchesTheMerchantFormula(long price, int count, int pack, int ratio, bool canSell, long expected) =>
            Assert.That(CraftAssist165.MerchantSellValue(price, count, pack, ratio, canSell), Is.EqualTo(expected));

        [TestCase("cloth square", 4, "cloth squares")]
        [TestCase("cloth square", 1, "cloth square")]
        [TestCase("metal bars", 3, "metal bars")]
        [TestCase("bounty", 2, "bounties")]
        [TestCase("ash", 2, "ashes")]
        public void Plural_ForChatLines(string name, int count, string expected) =>
            Assert.That(CraftAssist165.Plural(name, count), Is.EqualTo(expected));

        [Test]
        public void Command_ParsesSkillGoalAndFlags()
        {
            string[] args = { "&craftassist", "start", "11", "750", "1", "0" };
            Assert.That(CraftAssistCommandHandler.TryParse(args, 2, out eCraftingSkill s) && s == eCraftingSkill.Tailoring);
            Assert.That(CraftAssistCommandHandler.TryGoal(args, 3, out int goal) && goal == 750);
            Assert.That(CraftAssistCommandHandler.TryFlag(args, 4, out bool buy) && buy);
            Assert.That(CraftAssistCommandHandler.TryFlag(args, 5, out bool sell) && !sell);
            Assert.That(CraftAssistCommandHandler.TryFlag(args, 6, out _), Is.False, "missing flag");
            Assert.That(CraftAssistCommandHandler.TryParse(new[] { "x", "y", "5" }, 2, out _), Is.False, "no skill 5");
            Assert.That(CraftAssistCommandHandler.TryParse(new[] { "x", "y", "15" }, 2, out _), Is.False, "basic crafting");
            Assert.That(CraftAssistCommandHandler.TryParse(new[] { "x", "y", "0" }, 2, out _), Is.False);
            Assert.That(CraftAssistCommandHandler.TryGoal(new[] { "x", "y", "z", "0" }, 3, out _), Is.False);
            Assert.That(CraftAssistCommandHandler.TryGoal(new[] { "x", "y", "z", "1301" }, 3, out _), Is.False);
            Assert.That(CraftAssistCommandHandler.TryFlag(new[] { "2" }, 0, out _), Is.False);
        }
    }
}
