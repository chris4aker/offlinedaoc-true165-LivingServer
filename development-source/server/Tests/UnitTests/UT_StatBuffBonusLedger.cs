using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // owner 2026-11-10: a pulsing song renewal hands the bonus over; the replaced instance never touches it again
    public class UT_StatBuffBonusLedger
    {
        private const int Value = 6;
        private int _bonus;

        private void Start(StatBuffBonusLedger ledger, double effectiveness)
        {
            if (ledger.BeginApply(effectiveness)) _bonus += (int)(Value * effectiveness);
        }

        private void Stop(StatBuffBonusLedger ledger)
        {
            if (ledger.BeginRemove(out double applied)) _bonus -= (int)(Value * applied);
        }

        private void HandOver(StatBuffBonusLedger old, StatBuffBonusLedger next, double nextEffectiveness)
        {
            if (old.Supersede(out double applied)) _bonus -= (int)(Value * applied);
            if (next.BeginApply(nextEffectiveness)) _bonus += (int)(Value * nextEffectiveness);
        }

        [SetUp]
        public void Reset() => _bonus = 0;

        [Test]
        public void ReplacedInstanceEndedLaterDoesNotTakeTheBonusOffTwice()
        {
            StatBuffBonusLedger first = new(), second = new();
            Start(first, 1.0);
            HandOver(first, second, 1.0);
            Assert.That(_bonus, Is.EqualTo(Value));
            Stop(first);                 // the singer's pulse saw the replaced child expire
            Assert.That(_bonus, Is.EqualTo(Value));
            Stop(second);
            Assert.That(_bonus, Is.Zero);
        }

        [Test]
        public void StartStillQueuedWhenReplacedPutsNothingOn()
        {
            StatBuffBonusLedger first = new(), second = new();
            HandOver(first, second, 1.0); // the second singer's pulse in the same tick, before the first start ran
            Start(first, 1.0);            // the queued start runs afterwards
            Assert.That(_bonus, Is.EqualTo(Value));
            Stop(first);
            Stop(second);
            Assert.That(_bonus, Is.Zero);
        }

        [Test]
        public void RemovalUsesTheEffectivenessTheBonusWentOnWith()
        {
            StatBuffBonusLedger first = new(), second = new();
            Start(first, 0.5);            // caster still had resurrection illness
            HandOver(first, second, 1.0);
            Stop(second);
            Assert.That(_bonus, Is.Zero);
        }

        [Test]
        public void DisableAndEnableStaySymmetric()
        {
            StatBuffBonusLedger buff = new();
            Start(buff, 1.0);
            Stop(buff);
            Stop(buff);                   // a second stop is a no-op
            Start(buff, 1.0);
            Start(buff, 1.0);             // a second start is a no-op
            Assert.That(_bonus, Is.EqualTo(Value));
            Stop(buff);
            Assert.That(_bonus, Is.Zero);
        }

        [Test]
        public void ChainOfRenewalsEndsAtZero()
        {
            StatBuffBonusLedger a = new(), b = new(), c = new();
            Start(a, 1.0);
            HandOver(a, b, 1.0);
            HandOver(b, c, 1.0);
            Stop(a);
            Stop(b);
            Stop(c);
            Assert.That(_bonus, Is.Zero);
        }

        [TestCase(-6, false, true)]
        [TestCase(4, false, true)]
        [TestCase(-6, true, false)]
        [TestCase(0, false, false)]
        public void RegenBonusWithoutABuffAfterDeathIsDrift(int bonus, bool buffPresent, bool drift) =>
            Assert.That(BotRegenBonusRepair.IsDrift(bonus, buffPresent), Is.EqualTo(drift));
    }
}
