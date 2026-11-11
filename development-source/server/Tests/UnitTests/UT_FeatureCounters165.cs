using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace DOL.UnitTests.Gameserver;

/// <summary>FEATURE_COUNTERS_165 (2026-11-11): one summary line per five minutes of the features that log nothing per use.</summary>
[TestFixture, NonParallelizable]
public class UT_FeatureCounters165
{
    [TearDown]
    public void TearDown()
    {
        FeatureCounters165.Reset();
        FeatureCounters165.Clock = () => Environment.TickCount64;
        FeatureCounters165.Sink = null;
    }

    [Test]
    public void FeatureCountersWriteOneSummaryPerWindow()
    {
        long now = 1_000;
        var lines = new List<string>();
        FeatureCounters165.Reset();
        FeatureCounters165.Clock = () => now;
        FeatureCounters165.Sink = lines.Add;

        FeatureCounters165.Add("banter_realm");
        FeatureCounters165.Add("banter_realm");
        FeatureCounters165.Add("guild_dues_copper", 250);
        now += FeatureCounters165.WindowMilliseconds - 1;
        FeatureCounters165.Add("hot_stack_skip");
        Assert.That(lines, Is.Empty, "nothing before the window ends");

        now += 1;
        FeatureCounters165.Add("hot_stack_skip");
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0], Is.EqualTo(
            "FEATURE_COUNTERS_165 window=300s banter_realm=2 guild_dues_copper=250 hot_stack_skip=2"));

        now += FeatureCounters165.WindowMilliseconds;
        FeatureCounters165.Add("travel_upkeep_hold");
        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[1], Is.EqualTo("FEATURE_COUNTERS_165 window=300s travel_upkeep_hold=1"),
            "counts start again after each line; zero counters are left out");
    }

    [Test]
    public void BanterCounterNamesPerChannel() => Assert.Multiple(() =>
    {
        Assert.That(BotBanter165.CounterName("realm", false), Is.EqualTo("banter_realm"));
        Assert.That(BotBanter165.CounterName("guild", null), Is.EqualTo("banter_guild"));
        Assert.That(BotBanter165.CounterName("say", false), Is.EqualTo("banter_local"));
        Assert.That(BotBanter165.CounterName("group", true), Is.EqualTo("banter_party_reply"));
        Assert.That(BotBanter165.CounterName("realm", true), Is.EqualTo("banter_realm_reply"));
    });

    [Test]
    public void UselessPetBuffSkipsAreCounted()
    {
        long now = 0;
        var lines = new List<string>();
        FeatureCounters165.Reset();
        FeatureCounters165.Clock = () => now;
        FeatureCounters165.Sink = lines.Add;
        var acuity = new Spell(new DbSpell { Type = "AcuityBuff", Target = "Realm", Duration = 1200, Value = 10 }, 1);
        var strength = new Spell(new DbSpell { Type = "StrengthBuff", Target = "Realm", Duration = 1200, Value = 10 }, 1);

        FeatureCounters165.Add("start");
        Assert.That(BotPetBuffBenefit.HelpsCounted(acuity), Is.False);
        Assert.That(BotPetBuffBenefit.HelpsCounted(strength), Is.True);
        now = FeatureCounters165.WindowMilliseconds;
        FeatureCounters165.Add("start");
        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0], Does.Contain(" pet_buff_useless_skip=1"));
    }
}
