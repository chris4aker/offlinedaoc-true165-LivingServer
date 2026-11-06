using System;
using System.Linq;
using System.Threading.Tasks;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_BotBuffReservations
    {
        [Test]
        public void SixCastersCannotReserveTheSameBuffOnTheSameTarget()
        {
            var claims = new BotBuffReservations<object>();
            object target = new();
            var won = new bool[6];
            Parallel.For(0, 6, i => won[i] = claims.TryReserve(new(), target, 1, 0, 3000));
            Assert.That(won.Count(value => value), Is.EqualTo(1));
        }

        [Test]
        public void DifferentTargetsAndBuffFamiliesRemainAvailable()
        {
            var claims = new BotBuffReservations<object>();
            object caster = new(), player = new(), pet = new();
            Assert.That(claims.TryReserve(caster, player, 1, 0, 3000), Is.True);
            Assert.That(claims.TryReserve(new(), player, 2, 0, 3000), Is.True);
            Assert.That(claims.TryReserve(new(), pet, 1, 0, 3000), Is.True);
            Assert.That(new BotBuffReservations<object>().TryReserve(new(), player, 1, 0, 3000), Is.True);
        }

        [Test]
        public void FailedAndInterruptedCastsCannotLeavePermanentReservations()
        {
            var claims = new BotBuffReservations<object>();
            object caster = new(), target = new();
            claims.TryReserve(caster, target, 1, 0, 3000);
            claims.Release(new(), target, 1);
            Assert.That(claims.IsReserved(target, 1, 100), Is.True);
            claims.Release(caster, target, 1);
            Assert.That(claims.TryReserve(caster, target, 1, 100, 3000), Is.True);
            Assert.That(claims.IsReserved(target, 1, 5100), Is.False);
            Assert.That(claims.TryReserve(new(), target, 1, 5100, 3000), Is.True);
        }

        [Test]
        public void SelectionCanChooseAnyEligibleMemberIncludingTheLastPet()
        {
            object first = new(), buffed = new(), pet = new();
            object[] pool = [first, buffed, pet];
            Assert.That(BotBuffReservations<object>.Choose(pool, x => x != buffed, _ => 0), Is.SameAs(pet));
            Assert.That(BotBuffReservations<object>.Choose(pool, x => x != buffed, n => n - 1), Is.SameAs(first));
            Assert.That(BotBuffReservations<object>.Choose(pool, _ => false), Is.Null);
        }

        [Test]
        public void TheBestRankedEligibleMemberIsChosen()
        {
            object caster = new(), tank = new(), buffedTank = new(), melee = new();
            object[] pool = [caster, buffedTank, melee, tank];
            int Rank(object x) => x == caster ? 3 : x == melee ? 1 : 0;
            Assert.That(BotBuffReservations<object>.ChooseBest(pool, x => x != buffedTank, Rank, _ => 0), Is.SameAs(tank));
            Assert.That(BotBuffReservations<object>.ChooseBest(pool, x => x != buffedTank && x != tank, Rank, _ => 0), Is.SameAs(melee));
            Assert.That(BotBuffReservations<object>.ChooseBest(pool, _ => false, Rank), Is.Null);
        }
    }
}
