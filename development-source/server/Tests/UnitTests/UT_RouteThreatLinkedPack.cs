using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-10: a solo bot with no way around a GREEN route monster counts only the linked monsters (up to 3)
    /// when deciding to fight or give up; detours keep the wide pack; a solo bot goes around stealthed or ranged monsters
    /// when it can, and otherwise pulls them as before (design P6: never a new give-up).
    /// </summary>
    [TestFixture]
    public class UT_RouteThreatLinkedPack
    {
        private static readonly ConColor[] Cons =
            [ConColor.GREY, ConColor.GREEN, ConColor.BLUE, ConColor.YELLOW, ConColor.ORANGE, ConColor.RED, ConColor.PURPLE];

        [TestCase(1000f, 0f, 500, 500, ExpectedResult = true)]
        [TestCase(1100f, 0f, 500, 500, ExpectedResult = false)]
        [TestCase(300f, 0f, 100, 100, ExpectedResult = true, TestName = "InWidePack: the 320 floor")]
        [TestCase(300f, 200f, 100, 100, ExpectedResult = false, TestName = "InWidePack: too far above")]
        public bool WidePackIsTheOldFormula(float distance, float dz, int blockerAggro, int otherAggro) =>
            AutonomousRouteThreatPolicy.InWidePack(distance, dz, blockerAggro, otherAggro);

        [TestCase(560f, 0f, 500, 80, ExpectedResult = true)]
        [TestCase(600f, 0f, 500, 80, ExpectedResult = false)]
        [TestCase(100f, 161f, 500, 80, ExpectedResult = false)]
        public bool JoinsFightWhenItsOwnAggroCoversTheBlocker(float distance, float dz, int otherAggro, int margin) =>
            AutonomousRouteThreatPolicy.JoinsFight(distance, dz, otherAggro, margin);

        [TestCase(false, ConColor.GREEN, 7, 2, ExpectedResult = 2)]
        [TestCase(false, ConColor.GREEN, 7, 3, ExpectedResult = 3)]
        [TestCase(false, ConColor.GREEN, 7, 4, ExpectedResult = 7)]
        [TestCase(false, ConColor.BLUE, 5, 1, ExpectedResult = 5)]
        [TestCase(false, ConColor.YELLOW, 3, 1, ExpectedResult = 3)]
        [TestCase(true, ConColor.GREEN, 7, 1, ExpectedResult = 7)]
        public int DecisionPackOnlyForSoloGreen(bool grouped, ConColor con, int wide, int linked) =>
            AutonomousRouteThreatPolicy.DecisionPack(grouped, con, wide, linked);

        [Test]
        public void DecisionPackNeverAboveWide()
        {
            foreach (ConColor con in Cons)
            foreach (bool grouped in new[] { false, true })
            for (int wide = 1; wide <= 10; wide++)
            for (int linked = 1; linked <= 12; linked++)
                Assert.That(AutonomousRouteThreatPolicy.DecisionPack(grouped, con, wide, linked), Is.LessThanOrEqualTo(wide));
        }

        [Test]
        public void SwitchedOffGivesTheWidePack()
        {
            bool saved = AutonomousRouteThreatPolicy.LinkedPackEnabled;
            try
            {
                AutonomousRouteThreatPolicy.LinkedPackEnabled = false;
                Assert.That(AutonomousRouteThreatPolicy.DecisionPack(false, ConColor.GREEN, 7, 2), Is.EqualTo(7));
            }
            finally
            {
                AutonomousRouteThreatPolicy.LinkedPackEnabled = saved;
            }
        }

        [Test]
        public void GreenPackOfSevenWithTwoLinkedIsPulledInsteadOfGivingUp()
        {
            int pack = AutonomousRouteThreatPolicy.DecisionPack(false, ConColor.GREEN, 7, 2);
            Assert.That(AutonomousRouteThreatPolicy.Decide(ConColor.GREEN, false, pack, false, false, 0, true), Is.EqualTo(RouteThreatAction.Pull));
            Assert.That(AutonomousRouteThreatPolicy.Decide(ConColor.GREEN, false, 7, false, false, 0, true), Is.EqualTo(RouteThreatAction.Retarget));
        }

        [Test]
        public void PixieGuardDetourStaysWithOneLinked()
        {
            Assert.That(AutonomousRouteThreatPolicy.PreferDetour(RouteThreatAction.Pull, false, 3, false, true, 0), Is.EqualTo(RouteThreatAction.Detour));
            Assert.That(AutonomousRouteThreatPolicy.DecideRoute(ConColor.GREEN, false, 3, 1, false, true, 0, true, false, false, out int decisionPack),
                Is.EqualTo(RouteThreatAction.Detour));
            Assert.That(decisionPack, Is.EqualTo(1));
        }

        /// <summary>Critic 2026-11-10: PreferDetour is always given the wide pack (a solo Pull with a wide pack of 2+ and a
        /// usable detour always becomes a Detour, whatever the linked count).</summary>
        [Test]
        public void PreferDetourAlwaysGetsTheWidePack()
        {
            foreach (ConColor con in Cons)
            for (int wide = 2; wide <= 10; wide++)
            for (int linked = 1; linked <= wide; linked++)
            for (int detours = 0; detours < AutonomousRouteThreatPolicy.MaxDetoursPerThreat; detours++)
            foreach (bool mayRetarget in new[] { false, true })
            foreach (bool lowLevel in new[] { false, true })
            foreach (bool bossLike in new[] { false, true })
            {
                int decisionPack = AutonomousRouteThreatPolicy.DecisionPack(false, con, wide, linked);
                RouteThreatAction decided = AutonomousRouteThreatPolicy.Decide(con, false, decisionPack, bossLike, true, detours, mayRetarget, lowLevel);
                RouteThreatAction route = AutonomousRouteThreatPolicy.DecideRoute(con, false, wide, linked, bossLike, true, detours,
                    mayRetarget, lowLevel, false, out _);
                if (decided == RouteThreatAction.Pull)
                    Assert.That(route, Is.EqualTo(RouteThreatAction.Detour), $"{con} wide={wide} linked={linked}");
            }
        }

        /// <summary>The only change the linked count can make: a solo GREEN give-up (Retarget, or Ignore while the retarget
        /// cooldown runs) with no usable detour becomes a Pull. Never a new Retarget, never a lost Detour.</summary>
        [Test]
        public void ExhaustiveInvariantOnlyGreenSoloRetargetBecomesPull()
        {
            foreach (ConColor con in Cons)
            foreach (bool grouped in new[] { false, true })
            for (int wide = 1; wide <= 10; wide++)
            for (int linked = 1; linked <= wide; linked++)
            foreach (bool detour in new[] { false, true })
            for (int detours = 0; detours <= 2; detours++)
            foreach (bool mayRetarget in new[] { false, true })
            foreach (bool lowLevel in new[] { false, true })
            foreach (bool nearDeath in new[] { false, true })
            foreach (bool bossLike in new[] { false, true })
            {
                RouteThreatAction oldAction = AutonomousRouteThreatPolicy.PreferDetour(
                    AutonomousRouteThreatPolicy.Decide(con, grouped, wide, bossLike, detour, detours, mayRetarget, lowLevel),
                    grouped, wide, nearDeath, detour, detours);
                RouteThreatAction newAction = AutonomousRouteThreatPolicy.DecideRoute(con, grouped, wide, linked, bossLike,
                    detour, detours, mayRetarget, lowLevel, nearDeath, out _);
                string where = $"{con} grouped={grouped} wide={wide} linked={linked} detour={detour} detours={detours} " +
                               $"retarget={mayRetarget} low={lowLevel} death={nearDeath} boss={bossLike}";
                bool usableDetour = detour && detours < AutonomousRouteThreatPolicy.MaxDetoursPerThreat;
                Assert.That(newAction == oldAction ||
                            oldAction is RouteThreatAction.Retarget or RouteThreatAction.Ignore &&
                            newAction == RouteThreatAction.Pull && !grouped &&
                            con == ConColor.GREEN && !usableDetour, Is.True, where);
                Assert.That(newAction == RouteThreatAction.Retarget && oldAction != RouteThreatAction.Retarget, Is.False, "new Retarget: " + where);
                Assert.That(oldAction == RouteThreatAction.Detour && newAction != RouteThreatAction.Detour, Is.False, "lost Detour: " + where);
            }
        }

        [TestCase(RouteThreatAction.Pull, false, true, true, 0, ExpectedResult = RouteThreatAction.Detour, TestName = "Sneaky: solo goes around")]
        [TestCase(RouteThreatAction.Pull, false, true, false, 0, ExpectedResult = RouteThreatAction.Pull, TestName = "Sneaky: no way around, still pulls (P6)")]
        [TestCase(RouteThreatAction.Pull, false, true, true, 2, ExpectedResult = RouteThreatAction.Pull, TestName = "Sneaky: detours used up, still pulls")]
        [TestCase(RouteThreatAction.Pull, true, true, true, 0, ExpectedResult = RouteThreatAction.Pull, TestName = "Sneaky: groups unchanged")]
        [TestCase(RouteThreatAction.Pull, false, false, true, 0, ExpectedResult = RouteThreatAction.Pull, TestName = "Sneaky: ordinary monster unchanged")]
        [TestCase(RouteThreatAction.Retarget, false, true, true, 0, ExpectedResult = RouteThreatAction.Retarget, TestName = "Sneaky: only pulls change")]
        [TestCase(RouteThreatAction.Ignore, false, true, true, 0, ExpectedResult = RouteThreatAction.Ignore, TestName = "Sneaky: ignore stays ignore")]
        public RouteThreatAction StealthedOrRangedDetouredWhenPossible(RouteThreatAction decided, bool grouped, bool sneaky,
            bool detour, int detours) =>
            AutonomousRouteThreatPolicy.AvoidSneaky(decided, grouped, sneaky, detour, detours);

        /// <summary>Review 2026-11-10 (P6): after the whole route decision, AvoidSneaky only ever turns a solo Pull with a
        /// usable detour into a Detour. Never a new Retarget, never a new Ignore, never a change for a group.</summary>
        [Test]
        public void ExhaustiveInvariantSneakyOnlyAddsDetours()
        {
            foreach (ConColor con in Cons)
            foreach (bool grouped in new[] { false, true })
            for (int wide = 1; wide <= 8; wide++)
            for (int linked = 1; linked <= wide; linked++)
            foreach (bool detour in new[] { false, true })
            for (int detours = 0; detours <= 2; detours++)
            foreach (bool mayRetarget in new[] { false, true })
            foreach (bool lowLevel in new[] { false, true })
            foreach (bool nearDeath in new[] { false, true })
            foreach (bool bossLike in new[] { false, true })
            {
                RouteThreatAction route = AutonomousRouteThreatPolicy.DecideRoute(con, grouped, wide, linked, bossLike,
                    detour, detours, mayRetarget, lowLevel, nearDeath, out _);
                RouteThreatAction sneaky = AutonomousRouteThreatPolicy.AvoidSneaky(route, grouped, true, detour, detours);
                string where = $"{con} grouped={grouped} wide={wide} linked={linked} detour={detour} detours={detours} " +
                               $"retarget={mayRetarget} low={lowLevel} death={nearDeath} boss={bossLike}";
                bool usableDetour = detour && detours < AutonomousRouteThreatPolicy.MaxDetoursPerThreat;
                Assert.That(sneaky == route ||
                            route == RouteThreatAction.Pull && sneaky == RouteThreatAction.Detour && !grouped && usableDetour,
                    Is.True, where);
                Assert.That(sneaky == RouteThreatAction.Retarget && route != RouteThreatAction.Retarget, Is.False, "new Retarget: " + where);
                Assert.That(sneaky == RouteThreatAction.Ignore && route != RouteThreatAction.Ignore, Is.False, "new Ignore: " + where);
            }
        }

        /// <summary>Review 2026-11-10: passive (aggro level 0) monsters are dropped for solo bots only; group leaders keep them.</summary>
        [Test]
        public void AggroLevelFilterIsSoloOnly()
        {
            bool saved = AutonomousRouteThreatPolicy.RequireAggroLevel;
            try
            {
                AutonomousRouteThreatPolicy.RequireAggroLevel = true;
                Assert.That(AutonomousRouteThreatPolicy.CountsByAggroLevel(solo: true, aggroLevel: 0), Is.False);
                Assert.That(AutonomousRouteThreatPolicy.CountsByAggroLevel(solo: true, aggroLevel: 60), Is.True);
                Assert.That(AutonomousRouteThreatPolicy.CountsByAggroLevel(solo: false, aggroLevel: 0), Is.True);
                AutonomousRouteThreatPolicy.RequireAggroLevel = false;
                Assert.That(AutonomousRouteThreatPolicy.CountsByAggroLevel(solo: true, aggroLevel: 0), Is.True);
            }
            finally
            {
                AutonomousRouteThreatPolicy.RequireAggroLevel = saved;
            }
        }

        [Test]
        public void SneakyRuleSwitchedOff()
        {
            bool saved = AutonomousRouteThreatPolicy.NeverPullSneaky;
            try
            {
                AutonomousRouteThreatPolicy.NeverPullSneaky = false;
                Assert.That(AutonomousRouteThreatPolicy.AvoidSneaky(RouteThreatAction.Pull, false, true, true, 0),
                    Is.EqualTo(RouteThreatAction.Pull));
            }
            finally
            {
                AutonomousRouteThreatPolicy.NeverPullSneaky = saved;
            }
        }
    }
}
