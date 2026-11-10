using System.Linq;
using System.Numerics;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Owner 2026-11-10: bots off the bindstone after a release, /hood for companions, /stuck as a chosen death.</summary>
[TestFixture]
public class UT_StuckBindHood
{
    [Test]
    public void ARingAroundTheStoneIsEvenAndAtItsRadius()
    {
        Vector2[] ring = BotReleaseBindPoints.RingPoints(1000, 2000, 190, 8, 12345).ToArray();
        Assert.That(ring, Has.Length.EqualTo(8));
        Assert.That(ring.Select(p => Vector2.Distance(p, new Vector2(1000, 2000))), Has.All.EqualTo(190f).Within(0.01f));
        Assert.That(ring.Select(p => (int)p.X * 100000 + (int)p.Y).Distinct().Count(), Is.EqualTo(8), "eight different spots");
    }

    [Test]
    public void TheRingStartsWhereTheSeedSays()
    {
        Vector2 a = BotReleaseBindPoints.RingPoints(0, 0, 100, 8, 0).First();
        Vector2 b = BotReleaseBindPoints.RingPoints(0, 0, 100, 8, 90).First();
        Assert.That(a.X, Is.EqualTo(100f).Within(0.01f));
        Assert.That(b.Y, Is.EqualTo(100f).Within(0.01f));
        Assert.That(() => BotReleaseBindPoints.RingPoints(0, 0, 100, 8, int.MinValue).ToArray(), Throws.Nothing);
    }

    [Test]
    public void StuckIsRefusedWhenDeadOrFighting() => Assert.Multiple(() =>
    {
        Assert.That(StuckCommandHandler.Refusal(alive: true, inFight: false), Is.Null);
        Assert.That(StuckCommandHandler.Refusal(alive: true, inFight: true), Does.Contain("combat"));
        Assert.That(StuckCommandHandler.Refusal(alive: false, inFight: false), Does.Contain("dead"));
        Assert.That(StuckCommandHandler.Question, Does.Contain("no experience"));
    });

    [Test]
    public void ACompanionsHoodIsReadFromItsSavedOptions() => Assert.Multiple(() =>
    {
        Assert.That(CompanionOptions165.HoodUp(new OfflineHiredCompanionRecord { Options = "hood=1" }), Is.True);
        Assert.That(CompanionOptions165.HoodUp(new OfflineHiredCompanionRecord { Options = "charm=1;hood=1;pet=2" }), Is.True);
        Assert.That(CompanionOptions165.HoodUp(new OfflineHiredCompanionRecord { Options = "charm=1" }), Is.False);
        Assert.That(CompanionOptions165.HoodUp(new OfflineHiredCompanionRecord()), Is.False);
        Assert.That(CompanionOptions165.HoodUp(null), Is.False);
    });
}
