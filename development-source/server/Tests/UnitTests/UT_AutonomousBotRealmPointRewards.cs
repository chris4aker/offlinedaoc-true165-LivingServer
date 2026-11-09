using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_AutonomousBotRealmPointRewards
{
    private sealed class RewardServer : GameServer
    {
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private static readonly IObjectDatabase EmptyDatabase =
        DispatchProxy.Create<IObjectDatabase, EmptyReadDatabase>();

    public class EmptyReadDatabase : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type result = method.ReturnType;
            if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
            return null;
        }
    }

    private GameServer _previousServer;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((RewardServer)RuntimeHelpers.GetUninitializedObject(typeof(RewardServer)));
    }

    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_previousServer);

    private sealed class RewardBot : GameBot
    {
        private RewardBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override eRealm Realm { get; set; }
    }

    private static void SetField(Type type, object target, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static RewardBot Bot(bool autonomous, bool temporary)
    {
        RewardBot bot = (RewardBot)RuntimeHelpers.GetUninitializedObject(typeof(RewardBot));
        SetField(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
        SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
        typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, temporary);
        SetField(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
        return bot;
    }

    [Test]
    public void OnlyPersistentAutonomousBotIsRealmPointVictim()
    {
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(Bot(true, false)), Is.True);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(Bot(false, true)), Is.False,
            "/spawn companions must never be worth realm points");
        GameNPC ordinaryNpc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        SetField(typeof(GameNPC), ordinaryNpc, "m_brains", new ArrayList());
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(ordinaryNpc), Is.False,
            "ordinary NPCs and pets remain on the zero-RP NPC path");
    }

    [TestCase(25, 0, 25)]
    [TestCase(35, 0, 225)]
    [TestCase(35, 20, 245)]
    [TestCase(50, 0, 900)]
    [TestCase(50, 90, 990)]
    public void VictimValueMatchesPre181PlayerFormula(int level, int realmLevel, int expected)
    {
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentRealmPointValue(
            (byte)level, realmLevel), Is.EqualTo(expected));
    }

    [Test]
    public void RewardMatchesPlayerKillCapDamageRankAndGroupFormula()
    {
        int actual = AutonomousBotRealmPointRewards.CalculateRealmPointReward(
            victimRealmPointValue: 990,
            victimRealmLevel: 90,
            awarderRealmPointValue: 900,
            awarderRealmLevel: 0,
            participantCount: 2,
            groupContributorCount: 2,
            damagePercent: 0.75,
            applyRealmRankAdjustment: true);

        int baseShare = 990 / 2;
        int damageShare = (int)(baseShare * 0.75);
        int rankAdjusted = (int)(damageShare * (1.0 + 2.0 * 90 / 900.0));
        int expected = rankAdjusted + (int)(rankAdjusted * 0.125);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void CompanionAndControlledPetResolveToPlayerOwnerWithoutBecomingVictims()
    {
        GamePlayer owner = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
        RewardBot companion = Bot(false, true);
        typeof(GameBot).GetProperty(nameof(GameBot.Owner))!.SetValue(companion, owner);
        GameNPC pet = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        SetField(typeof(GameNPC), pet, "m_brains", new ArrayList());
        SetField(typeof(GameNPC), pet, "m_ownBrain", new ControlledMobBrain(companion) { Body = pet });

        Assert.That(AutonomousBotRealmPointRewards.ResolveRootRewardOwner(companion), Is.SameAs(owner));
        Assert.That(AutonomousBotRealmPointRewards.ResolveRootRewardOwner(pet), Is.SameAs(owner));
        Assert.That(AutonomousRvrDefense.IsCombatant(owner), Is.True);
        Assert.That(AutonomousRvrDefense.IsCombatant(companion), Is.True);
        Assert.That(AutonomousRvrDefense.IsCombatant(pet), Is.True);
        SetField(typeof(GameNPC), pet, "m_ownBrain", new ControlledMobBrain(owner) { Body = pet });
        Assert.That(AutonomousRvrDefense.IsCombatant(pet), Is.True, "Direct human-owned pets are not IGamePlayer but must be defended against");
        Assert.That(AutonomousRvrDefense.IsCombatant(null), Is.False);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(companion), Is.False);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(pet), Is.False);
    }

    // Experience for an enemy-realm kill (owner 2026-11-09): the player-kill rule - the victim's value split among the
    // contributing group members, capped at 125% of the awarder's own level's value, times the damage share.
    [Test]
    public void ExperienceRewardFollowsThePlayerKillRule()
    {
        // alone, all the damage, same level: the whole value
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(4000, 4000, 125, 1, 1.0), Is.EqualTo(4000));
        // a far higher victim: capped at 125% of what the awarder's own level is worth
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(40000, 4000, 125, 1, 1.0), Is.EqualTo(5000));
        // two of a group did three quarters of the damage: each gets half the value times the group's share
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(4000, 4000, 125, 2, 0.75), Is.EqualTo(1500));
        // no damage, no experience; a share over 100% counts as 100%
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(4000, 4000, 125, 1, 0.0), Is.EqualTo(0));
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(4000, 4000, 125, 1, 1.5), Is.EqualTo(4000));
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(0, 4000, 125, 1, 1.0), Is.EqualTo(0));
        Assert.That(AutonomousBotRealmPointRewards.CalculateExperienceReward(4000, 4000, 125, 0, 1.0), Is.EqualTo(0));
    }

    [Test]
    public void OneKillNeverPaysMoreExperienceThanTheVictimIsWorth()
    {
        // three separate enemies shared the kill 50 / 30 / 20
        long paid = AutonomousBotRealmPointRewards.CalculateExperienceReward(4000, 4000, 125, 1, 0.5) +
                    AutonomousBotRealmPointRewards.CalculateExperienceReward(4000, 4000, 125, 1, 0.3) +
                    AutonomousBotRealmPointRewards.CalculateExperienceReward(4000, 4000, 125, 1, 0.2);
        Assert.That(paid, Is.LessThanOrEqualTo(4000));
        // a group of four that did everything: four quarter shares
        Assert.That(4 * AutonomousBotRealmPointRewards.CalculateExperienceReward(4000, 4000, 125, 4, 1.0), Is.EqualTo(4000));
    }

    [TestCase(1)]
    [TestCase(24)]
    [TestCase(50)]
    public void GamebotIsWorthAPlayersExperience(int level)
    {
        // GamePlayer.ExperienceValue: four times a monster of its level
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentExperienceValue(level),
            Is.EqualTo(GameServer.ServerRules.GetExperienceForLiving(level) * 4));
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentExperienceValue(level), Is.GreaterThan(0));
    }
}
