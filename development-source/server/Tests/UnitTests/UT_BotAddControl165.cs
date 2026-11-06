using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// True 1.65 add control (owner 2026-11-06: a Healer never mezzed, whatever its Pacification): a monster only targeted
    /// by a tank running at it is still an add, the add being mezzed is left alone for a moment, add control comes before
    /// a top-up heal, and the instant single-target mez goes on a single add when the caster cannot finish a cast.
    /// </summary>
    [TestFixture]
    public sealed class UT_BotAddControl165
    {
        [Test]
        public void AnAddIsHandledOnlyWhenSomebodyHitsItOrItIsTheKillTarget() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.IsHandledAdd(true, targeted: true, attacked: false, killTarget: false, ownTarget: false), Is.False,
                "a tank's protection has it targeted, nobody hits it yet: mezzable");
            Assert.That(BotBrain.IsHandledAdd(true, targeted: true, attacked: true, killTarget: false, ownTarget: false), Is.True);
            Assert.That(BotBrain.IsHandledAdd(true, targeted: false, attacked: false, killTarget: true, ownTarget: false), Is.True);
            Assert.That(BotBrain.IsHandledAdd(true, targeted: false, attacked: false, killTarget: false, ownTarget: true), Is.False,
                "the healer's own target is not a fight it is in");
            Assert.That(BotBrain.IsHandledAdd(false, targeted: true, attacked: false, killTarget: false, ownTarget: false), Is.True,
                "normal mode unchanged");
            Assert.That(BotBrain.IsHandledAdd(false, targeted: false, attacked: false, killTarget: false, ownTarget: true), Is.True,
                "normal mode unchanged");
        });

        [Test]
        public void TheInstantMezIsKeptForEmergenciesOrASingleAddOnACasterWhoCannotCast() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.AllowsLongReuseControl(true, emergency: true, singleTarget: false, castTimeBlocked: false), Is.True);
            Assert.That(BotBrain.AllowsLongReuseControl(true, emergency: false, singleTarget: true, castTimeBlocked: true), Is.True,
                "Pacifying Glance on the add hitting the healer");
            Assert.That(BotBrain.AllowsLongReuseControl(true, emergency: false, singleTarget: true, castTimeBlocked: false), Is.False,
                "the timed mez goes off: the instant one is saved");
            Assert.That(BotBrain.AllowsLongReuseControl(true, emergency: false, singleTarget: false, castTimeBlocked: true), Is.False,
                "the instant area mez stays an emergency spell");
            Assert.That(BotBrain.AllowsLongReuseControl(false, emergency: false, singleTarget: true, castTimeBlocked: true), Is.False,
                "normal mode unchanged");
        });

        [Test]
        public void AddControlComesBeforeHealsUnlessSomeoneIsUnder40() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.AddControlBeforeHeals(true, 60), Is.True);
            Assert.That(BotBrain.AddControlBeforeHeals(true, BotBrain.AddControlBeforeHealsHealthPercent), Is.True);
            Assert.That(BotBrain.AddControlBeforeHeals(true, 39), Is.False);
            Assert.That(BotBrain.AddControlBeforeHeals(false, 100), Is.False, "normal mode: heals first");
        });

        [Test]
        public void AMezReservationHoldsUntilItEndsOrIsReleased()
        {
            object add = new(), other = new();
            Assert.That(BotMezReservations.IsReserved(add, 1_000), Is.False);
            BotMezReservations.Reserve(add, 4_000);
            Assert.Multiple(() =>
            {
                Assert.That(BotMezReservations.IsReserved(add, 1_000), Is.True);
                Assert.That(BotMezReservations.IsReserved(add, 4_000), Is.False, "ends after about three seconds");
                Assert.That(BotMezReservations.IsReserved(other, 1_000), Is.False);
                Assert.That(BotMezReservations.IsReserved(null, 1_000), Is.False);
            });
            BotMezReservations.Release(add);
            Assert.That(BotMezReservations.IsReserved(add, 1_000), Is.False, "a cast that did not start frees it");
        }
    }
}
