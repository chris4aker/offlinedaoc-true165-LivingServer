using NUnit.Framework;
using static DOL.GS.Tests.RealmGateTestKit;

namespace DOL.GS.Tests;

/// <summary>
/// True 1.65 Mode milegates ("gate forts") and border keep doors (owner 2026-11-06): owned by a realm, its realm passes,
/// enemies cannot open them and break them like keep doors; a broken gate stands open until repaired.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_RealmGates165 : RealmGateFixture
{
    [Test]
    public void OnlyRealmOwnedDoorsOutsideKeepsLoadAsGatesInTrue165Mode()
    {
        Assert.That(GameRealmGate.LoadsAsGate(true, false, eRealm.Albion), Is.True);
        Assert.That(GameRealmGate.LoadsAsGate(true, false, eRealm.Midgard), Is.True);
        Assert.That(GameRealmGate.LoadsAsGate(true, false, eRealm.Hibernia), Is.True);
        // Houses, inns, towns and dungeons: every other door is realm 6 ("Door") and stays a GameDoor.
        Assert.That(GameRealmGate.LoadsAsGate(true, false, eRealm.Door), Is.False);
        Assert.That(GameRealmGate.LoadsAsGate(true, false, eRealm.None), Is.False);
        Assert.That(GameRealmGate.LoadsAsGate(true, true, eRealm.Albion), Is.False, "a keep's door is a GameKeepDoor");
        Assert.That(GameRealmGate.LoadsAsGate(false, false, eRealm.Albion), Is.False, "normal mode unchanged");
    }

    [Test]
    public void GateFortGateLetsItsRealmThroughAndShutsEnemiesOut()
    {
        Assert.That(GameRealmGate.MayPass(eRealm.Albion, eRealm.Albion, eDoorState.Closed), Is.True);
        Assert.That(GameRealmGate.MayPass(eRealm.Midgard, eRealm.Albion, eDoorState.Closed), Is.False);
        Assert.That(GameRealmGate.MayPass(eRealm.None, eRealm.Albion, eDoorState.Closed), Is.False, "monsters stay out too");
        // Broken or opened: it stands open for everyone.
        Assert.That(GameRealmGate.MayPass(eRealm.Midgard, eRealm.Albion, eDoorState.Open), Is.True);
        Assert.That(NewGate(eRealm.Albion).MayPass(NewBot(eRealm.Albion)), Is.True);
        Assert.That(NewGate(eRealm.Albion).MayPass(NewBot(eRealm.Hibernia)), Is.False);
    }

    [Test]
    public void OnlyAnEnemyRealmBreaksAStandingShutGate()
    {
        Assert.That(GameRealmGate.MayBreak(eRealm.Hibernia, eRealm.Midgard, true, eDoorState.Closed), Is.True);
        Assert.That(GameRealmGate.MayBreak(eRealm.Midgard, eRealm.Midgard, true, eDoorState.Closed), Is.False);
        Assert.That(GameRealmGate.MayBreak(eRealm.Hibernia, eRealm.Midgard, false, eDoorState.Open), Is.False, "already broken");
        Assert.That(GameRealmGate.MayBreak(eRealm.Hibernia, eRealm.Midgard, true, eDoorState.Open), Is.False, "standing open");
        Assert.That(GameRealmGate.MayBreak(eRealm.None, eRealm.Midgard, true, eDoorState.Closed), Is.False);
    }

    [Test]
    public void GateIsAsStrongAsALevelOneFrontierKeepDoorAndStaysOpenWhileBroken()
    {
        Assert.That(GameRealmGate.GateMaxHealth(200), Is.EqualTo(10_000), "keep_doors_base_health 200 x base level 50");
        Assert.That(GameRealmGate.GateArmorFactor, Is.EqualTo(1_320), "base level 50 x level-1 modifier 1.1 x 24");
        Assert.That(GameRealmGate.AdjustedDamage(100, 100), Is.EqualTo(95), "keep door rule at keep level 1");
        Assert.That(GameRealmGate.IsBrokenPercent(0), Is.True);
        Assert.That(GameRealmGate.IsBrokenPercent(15), Is.True, "repaired to 15% it still stands open");
        Assert.That(GameRealmGate.IsBrokenPercent(16), Is.False);
    }

    [Test]
    public void GateIsAnAttackableDoorNobodyOpensByInteraction()
    {
        Gate gate = NewGate(eRealm.Albion);
        Assert.That(gate.IsAttackableDoor, Is.True);
        Assert.That(gate.CanBeOpenedViaInteraction, Is.False, "the navmesh registers it and shuts its polygons");
    }

    [Test]
    public void BotsTreatAGateAsADoorToBreach()
    {
        Assert.That(AutonomousRvrObjectiveState.ForTarget(NewGate(eRealm.Albion)), Is.EqualTo(eAutonomousRvrPhase.BreachingDoor));
    }
}
