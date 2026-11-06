using DOL.Database;
using NUnit.Framework;
using static DOL.GS.BotPetFocusShieldPolicy;

namespace DOL.GS.Tests
{
    /// <summary>The pet focus damage shield, used as a player uses it (owner 2026-10-28).</summary>
    [TestFixture]
    public sealed class UT_BotPetFocusShieldPolicy
    {
        private static bool Start(bool petHit = true, int targetHealth = 100, bool casterHit = false, bool players = false,
            bool mustMove = false, int power = 50, bool nothingElse = false, bool shielded = false, long now = 20_000,
            long backoff = 0) =>
            ShouldStart(petHit, targetHealth, casterHit, players, mustMove, power, nothingElse, shielded, now, backoff);

        [Test]
        public void OnlyAPetBeingHitIsShielded() => Assert.Multiple(() =>
        {
            Assert.That(Start(), Is.True, "soloing, the pet tanking");
            Assert.That(Start(petHit: false), Is.False, "a real tank holds the monster: nothing to reflect");
        });

        [Test]
        public void NotWhenItWouldBeBrokenOrWasted() => Assert.Multiple(() =>
        {
            Assert.That(Start(casterHit: true), Is.False, "a hit breaks the channel");
            Assert.That(Start(players: true), Is.False, "enemy players about");
            Assert.That(Start(mustMove: true), Is.False, "the group is moving on");
            Assert.That(Start(targetHealth: 30), Is.False, "the fight is nearly over");
            Assert.That(Start(shielded: true), Is.False, "already shielded");
            Assert.That(Start(now: 5_000, backoff: 10_000), Is.False, "waiting after a broken channel");
        });

        [Test]
        public void ItSavesPowerAndNukesAreBetterWithPowerToSpare() => Assert.Multiple(() =>
        {
            Assert.That(Start(power: 80), Is.False, "plenty of power: nuke instead");
            Assert.That(Start(power: 80, nothingElse: true), Is.True, "nothing else to cast");
            Assert.That(Start(power: 10, nothingElse: true), Is.False, "too little power to channel");
        });

        [Test]
        public void True165OpenerShieldsThePetAtAnyPowerEarlyInTheFight() => Assert.Multiple(() =>
        {
            Assert.That(ShouldStart(true, 100, false, false, false, 100, false, false, 20_000, 0, opener: true), Is.True,
                "full power: the shield first so the pet holds the monster");
            Assert.That(ShouldStart(false, 100, false, false, false, 100, false, false, 20_000, 0, opener: true), Is.False,
                "not while a real tank holds it");
            Assert.That(ShouldStart(true, 100, false, false, false, 10, false, false, 20_000, 0, opener: true), Is.False,
                "still not under 20% power");
            Assert.That(InOpenerWindow(0), Is.True);
            Assert.That(InOpenerWindow(OpenerMilliseconds), Is.True);
            Assert.That(InOpenerWindow(OpenerMilliseconds + 1), Is.False, "later in the fight the caster nukes");
        });

        [Test]
        public void AnOpenerWithPowerToSpareIsBrokenForNukes() => Assert.Multiple(() =>
        {
            Assert.That(OpenerDone(true, 90, OpenerChannelMilliseconds), Is.True);
            Assert.That(OpenerDone(true, 90, OpenerChannelMilliseconds - 1), Is.False, "the pet takes the monster first");
            Assert.That(OpenerDone(true, MaxPowerPercent, 30_000), Is.False, "power to save now: keep channelling");
            Assert.That(OpenerDone(false, 90, 30_000), Is.False, "an ordinary channel");
        });

        [Test]
        public void TheChannelIsDroppedWhenItDoesNothingOrMustEnd() => Assert.Multiple(() =>
        {
            Assert.That(ShouldKeep(true, false, false, 40, 30_000), Is.True);
            Assert.That(ShouldKeep(false, false, false, 40, 30_000), Is.False, "nobody hits the pet (the fight is over)");
            Assert.That(ShouldKeep(true, true, false, 40, 30_000), Is.False, "the caster is hit");
            Assert.That(ShouldKeep(true, false, true, 40, 30_000), Is.False, "moving on");
            Assert.That(ShouldKeep(true, false, false, KeepPowerPercent - 1, 30_000), Is.False, "out of power");
            Assert.That(ShouldKeep(true, false, false, 40, MaxChannelMilliseconds), Is.False, "never forever");
        });

        [Test]
        public void OnlyThePetFocusShieldCounts()
        {
            Spell focus = new(new DbSpell { Type = "DamageShield", Target = "Pet", IsFocus = true, Duration = 5, Frequency = 50, Pulse = 1 }, 1);
            Spell timed = new(new DbSpell { Type = "DamageShield", Target = "Pet", Duration = 30 }, 1);
            Assert.That(IsPetFocusShield(focus), Is.True);
            Assert.That(IsPetFocusShield(timed), Is.False);
        }
    }
}
