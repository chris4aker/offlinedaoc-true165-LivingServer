using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>True 1.65 Mode: an RvR bot breaks the enemy milegate its route stopped at (AutonomousRouteGates).</summary>
[TestFixture, NonParallelizable]
public class UT_RouteGates165 : RealmGateFixture
{
    [Test]
    public void BotBattersOnlyAFreshNearEnemyGate()
    {
        const long now = 1_000_000;
        Assert.That(AutonomousRouteGates.ShouldBatter(eRealm.Albion, eRealm.Midgard, true, eDoorState.Closed, 300, now - 1_000, now), Is.True);
        Assert.That(AutonomousRouteGates.ShouldBatter(eRealm.Midgard, eRealm.Midgard, true, eDoorState.Closed, 300, now - 1_000, now), Is.False, "own gate");
        Assert.That(AutonomousRouteGates.ShouldBatter(eRealm.Albion, eRealm.Midgard, true, eDoorState.Closed, 5_000, now - 1_000, now), Is.False, "far away");
        Assert.That(AutonomousRouteGates.ShouldBatter(eRealm.Albion, eRealm.Midgard, true, eDoorState.Closed, 300, now - 60_000, now), Is.False, "stale");
        Assert.That(AutonomousRouteGates.ShouldBatter(eRealm.Albion, eRealm.Midgard, true, eDoorState.Closed, 300, 0, now), Is.False, "never met");
        Assert.That(AutonomousRouteGates.ShouldBatter(eRealm.Albion, eRealm.Midgard, false, eDoorState.Open, 300, now - 1_000, now), Is.False, "broken");
        Assert.That(AutonomousRouteGates.ShouldBatter(eRealm.Albion, eRealm.Midgard, true, eDoorState.Open, 300, now - 1_000, now), Is.False,
            "opened by its realm just now: the route goes on through it");
    }

    [Test]
    public void TheRouteStopsWithinBatteringReachOfTheGate()
    {
        Assert.That(GameRealmGate.CrossingRadius, Is.LessThan(AutonomousRouteGates.BatterReach));
        Assert.That(GameRealmGate.CrossingRadius, Is.LessThan(200), "inside an NPC's melee reach (GameLiving.MeleeAttackRange 200)");
    }
}
