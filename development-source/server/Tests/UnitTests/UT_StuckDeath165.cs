using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Owner 2026-11-10: a /stuck death costs no experience, Constitution or resurrection sickness - and every other death
/// must still be paid in full. The death is processed a tick after /stuck (the ReaperService queue), so the stamp has to
/// survive until then; it counts only for a death with no killer within 2 seconds.
/// </summary>
[TestFixture]
public class UT_StuckDeath165
{
    private const long Now = 1_000_000;

    [Test]
    public void TheDeathProcessedATickAfterStuckIsTheStuckDeath() => Assert.Multiple(() =>
    {
        Assert.That(StuckDeath165.IsStuckDeath(Now, Now, true), Is.True, "same tick");
        Assert.That(StuckDeath165.IsStuckDeath(Now, Now + 50, true), Is.True, "the next game-loop tick");
        Assert.That(StuckDeath165.IsStuckDeath(Now, Now + StuckDeath165.WindowMilliseconds, true), Is.True);
    });

    [Test]
    public void ANormalDeathIsNeverSpared() => Assert.Multiple(() =>
    {
        Assert.That(StuckDeath165.IsStuckDeath(0, Now, true), Is.False, "no /stuck at all");
        Assert.That(StuckDeath165.IsStuckDeath(0, Now, false), Is.False, "killed by a monster, no /stuck");
        Assert.That(StuckDeath165.IsStuckDeath(Now, Now + StuckDeath165.WindowMilliseconds + 1, true), Is.False,
            "an old stamp never spares a later death");
        Assert.That(StuckDeath165.IsStuckDeath(Now, Now + 300_000, true), Is.False, "minutes later");
        Assert.That(StuckDeath165.IsStuckDeath(Now, Now + 100, false), Is.False, "a monster's kill in the same moment pays in full");
        Assert.That(StuckDeath165.IsStuckDeath(Now, Now - 10, true), Is.False, "a stamp from the future is ignored");
    });

    [Test]
    public void TheStuckQuestionSaysWhatItCostsAndWhatNot()
    {
        Assert.That(StuckCommandHandler.Question, Does.Contain("no experience"));
        Assert.That(StuckCommandHandler.Question, Does.Contain("Constitution"));
        Assert.That(StuckCommandHandler.Question, Does.Contain("sickness"));
    }
}
