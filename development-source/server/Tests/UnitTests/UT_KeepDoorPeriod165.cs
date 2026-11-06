using DOL.GS.Keeps;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using static DOL.GS.Tests.RealmGateTestKit;

namespace DOL.GS.Tests;

/// <summary>True 1.65 Mode keep doors, the period rules (owner 2026-11-06): a keep door never opens by hand; its realm's
/// players are ported through it (the click, Interact), enemies break it; a portal keep's doors cannot be damaged.</summary>
[TestFixture, NonParallelizable]
public class UT_KeepDoorPeriod165 : RealmGateFixture
{
    [Test]
    public void AKeepDoorRequestPortsInsteadOfOpening()
    {
        Assert.That(GameKeepDoor.PortsOnRequest(classic165: true, towerWithComponents: false), Is.True, "True 1.65: the click ports");
        Assert.That(GameKeepDoor.PortsOnRequest(classic165: false, towerWithComponents: false), Is.False, "normal mode unchanged");
        Assert.That(GameKeepDoor.PortsOnRequest(classic165: false, towerWithComponents: true), Is.True, "normal mode towers unchanged");
    }

    [Test]
    public void FrontierRelicAndBattlegroundKeepDoorsAreBrokenPortalKeepDoorsAreNot()
    {
        Assert.That(GameKeepDoor.IsAttackableKeepDoor(classic165: true, portalKeep: false, postern: false), Is.True);
        Assert.That(GameKeepDoor.IsAttackableKeepDoor(classic165: true, portalKeep: true, postern: false), Is.False);
        Assert.That(GameKeepDoor.IsAttackableKeepDoor(classic165: true, portalKeep: false, postern: true), Is.False, "posterns as before");
        Assert.That(GameKeepDoor.IsAttackableKeepDoor(classic165: false, portalKeep: true, postern: false), Is.True, "normal mode unchanged");
    }

    [Test]
    public void PortalKeepDoorPortsItsRealmAndCannotBeBroken()
    {
        KeepDoor door = NewKeepDoor(3, portalKeep: true);
        Properties.CLASSIC165_MODE = true;
        Assert.That(door.IsAttackableDoor, Is.False, "you can't take a ram to a PK door");
        Assert.That(door.Health, Is.Zero, "no health: every attack is refused");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Hibernia), door), Is.False, "own realm (traverse)");
        Assert.That(Pathfinder.BlocksActor(NewBot(eRealm.Midgard), door), Is.True, "enemy bot");
        Properties.CLASSIC165_MODE = false;
        Assert.That(door.IsAttackableDoor, Is.True, "normal mode unchanged");
    }

    [Test]
    public void FightersReachAnAttackableDoorFromItsDoorway()
    {
        // Client live test 2026-11-06: "too far away to attack" ~2 m out from a big double gate (128 reach to the row's point).
        Properties.CLASSIC165_MODE = true;
        Assert.That(GameDoorBase.MeleeReachBonus(NewKeepDoor(2), classic165: true), Is.EqualTo(GameDoorBase.DoorwayReach), "keep door");
        Assert.That(GameDoorBase.MeleeReachBonus(NewGate(eRealm.Albion), classic165: true), Is.EqualTo(GameDoorBase.DoorwayReach), "milegate");
        Assert.That(GameDoorBase.MeleeReachBonus(NewGate(eRealm.Albion, doorId: BorderKeepId), classic165: true), Is.Zero,
            "a border keep door is never attacked");
        Assert.That(GameDoorBase.MeleeReachBonus(NewKeepDoor(3, portalKeep: true), classic165: true), Is.Zero, "nor a portal keep door");
        Assert.That(GameDoorBase.MeleeReachBonus(NewBot(eRealm.Albion), classic165: true), Is.Zero, "door targets only");
        Assert.That(GameDoorBase.MeleeReachBonus(NewKeepDoor(2), classic165: false), Is.Zero, "normal mode unchanged");
        Assert.That(128 + GameDoorBase.DoorwayReach, Is.GreaterThan(117 + 80), "half a 234-wide gate plus 2 m");
    }
}
