using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Owner 2026-11-09 (0.6): gamebot guildmates did not show in /gc who; their lines read as the players' do.</summary>
[TestFixture]
public class UT_GuildWhoLine
{
    [Test]
    public void LineReadsLikeAPlayerLine()
    {
        Assert.That(GuildCommandHandler.GuildWhoLine(3, "Astrenfrid", "Member", 12, "Healer", "Vale of Mularn"),
            Is.EqualTo("3) Astrenfrid <Member> the Level 12 Healer in Vale of Mularn"));
    }

    [Test]
    public void MissingClassOrZoneStillMakesALine()
    {
        Assert.That(GuildCommandHandler.GuildWhoLine(1, "Bot", "9", 1, null, null), Is.EqualTo("1) Bot <9> the Level 1 Unknown in (null)"));
    }
}
