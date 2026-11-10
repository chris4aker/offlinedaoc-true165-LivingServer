using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Owner 2026-11-09 (0.6): invited gamebots pay their guild's dues on looted coin, like player members.</summary>
[TestFixture]
public class UT_BotGuildDues
{
    [TestCase(70, 5, 3)]      // the tester's own kill: 70 copper looted, 3 to the bank
    [TestCase(100, 25, 25)]   // the highest dues the server allows
    [TestCase(10000, 2, 200)]
    [TestCase(3, 25, 0)]      // too little to take a copper from
    public void DuesAreThePercentOfTheLootedCoin(long money, long percent, long expected)
    {
        Assert.That(AutonomousBotGuilds.DuesOf(money, true, percent), Is.EqualTo(expected));
    }

    [Test]
    public void NoDuesWhenOffOrNothingLooted()
    {
        Assert.That(AutonomousBotGuilds.DuesOf(1000, false, 25), Is.EqualTo(0), "dues switched off");
        Assert.That(AutonomousBotGuilds.DuesOf(1000, true, 0), Is.EqualTo(0), "0 percent");
        Assert.That(AutonomousBotGuilds.DuesOf(0, true, 25), Is.EqualTo(0));
        Assert.That(AutonomousBotGuilds.DuesOf(-5, true, 25), Is.EqualTo(0));
    }

    [Test]
    public void DuesNeverTakeMoreThanTheCoin()
    {
        Assert.That(AutonomousBotGuilds.DuesOf(1000, true, 250), Is.EqualTo(1000));
    }
}
