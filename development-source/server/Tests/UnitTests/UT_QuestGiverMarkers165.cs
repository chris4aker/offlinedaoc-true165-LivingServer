using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_QuestGiverMarkers165
{
    // 2026-11-05: the list kept its build time from long.MinValue; "now - long.MinValue" overflowed to a negative number,
    // so the empty list looked fresh forever and no quest giver marker was ever sent.
    [Test]
    public void TheGiverListIsBuiltOnTheFirstCallAndAgainWhenDue()
    {
        long now = Environment.TickCount64;
        Assert.Multiple(() =>
        {
            Assert.That(QuestGiverMarkers165.GiverListDue(now, 0), Is.True, "first call");
            Assert.That(QuestGiverMarkers165.GiverListDue(now, now + 600_000), Is.False, "just built");
            Assert.That(QuestGiverMarkers165.GiverListDue(now + 600_000, now + 600_000), Is.True, "ten minutes on");
        });
    }
}
