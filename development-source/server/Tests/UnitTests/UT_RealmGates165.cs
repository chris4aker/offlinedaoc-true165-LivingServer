using NUnit.Framework;
using static DOL.GS.Tests.RealmGateTestKit;

namespace DOL.GS.Tests;

/// <summary>
/// True 1.65 Mode realm gates, the period rules (owner 2026-11-06): a milegate ("gate fort") opens for anyone and can be
/// attacked, broken and repaired; a border keep door cannot be damaged and only its realm opens it.
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
    public void BorderKeepDoorsAreKnownByTheirFixture()
    {
        foreach (int id in new[] { 11020501, 11020502, 11020503, 11020504, 12000101, 12000104, 102093502, 111161301, 206016802, 207156901 })
            Assert.That(GameRealmGate.IsBorderKeepFixture(id), Is.True, id.ToString());
        foreach (int id in new[] { 15000401, 15000504, 115000101, 115154303, 214000202 })
            Assert.That(GameRealmGate.IsBorderKeepFixture(id), Is.False, id.ToString());
    }

    [Test]
    public void AnyoneOpensAMilegateAndWalksThrough()
    {
        foreach (eRealm actor in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            Assert.That(GameRealmGate.MayOpen(actor, eRealm.Albion, borderKeep: false), Is.True, $"{actor} opens Albion's milegate");
            Assert.That(GameRealmGate.MayPass(actor, eRealm.Albion, eDoorState.Closed, borderKeep: false), Is.True, $"{actor} walks through it shut");
        }
        Gate milegate = NewGate(eRealm.Albion);
        Assert.That(milegate.IsMilegate, Is.True);
        Assert.That(milegate.CanBeOpenedViaInteraction, Is.True, "never registered on the navmesh, never a blocker");
        Assert.That(milegate.MayOpen(NewBot(eRealm.Midgard)), Is.True);
        Assert.That(milegate.MayOpen(NewBot(eRealm.Albion)), Is.True);
    }

    [Test]
    public void AMilegateCanStillBeAttackedBrokenAndRepaired()
    {
        Gate milegate = NewGate(eRealm.Albion);
        Assert.That(milegate.IsAttackableDoor, Is.True);
        Assert.That(GameRealmGate.MayBreak(eRealm.Midgard, eRealm.Albion, alive: true, borderKeep: false), Is.True);
        Assert.That(GameRealmGate.MayBreak(eRealm.Albion, eRealm.Albion, alive: true, borderKeep: false), Is.False, "its own realm repairs it");
        Assert.That(GameRealmGate.MayBreak(eRealm.Midgard, eRealm.Albion, alive: false, borderKeep: false), Is.False, "already broken");
        Assert.That(GameRealmGate.GateMaxHealth(200), Is.EqualTo(10_000), "keep_doors_base_health 200 x base level 50");
        Assert.That(GameRealmGate.GateArmorFactor, Is.EqualTo(1_320), "base level 50 x level-1 modifier 1.1 x 24");
        Assert.That(GameRealmGate.AdjustedDamage(100, 100), Is.EqualTo(95), "keep door rule at keep level 1");
        Assert.That(GameRealmGate.IsBrokenPercent(15), Is.True, "repaired to 15% it still stands open");
        Assert.That(GameRealmGate.IsBrokenPercent(16), Is.False);
    }

    [Test]
    public void OnlyItsRealmOpensABorderKeepDoor()
    {
        Assert.That(GameRealmGate.MayOpen(eRealm.Albion, eRealm.Albion, borderKeep: true), Is.True);
        Assert.That(GameRealmGate.MayOpen(eRealm.Midgard, eRealm.Albion, borderKeep: true), Is.False);
        Assert.That(GameRealmGate.MayOpen(eRealm.None, eRealm.Albion, borderKeep: true), Is.False);
        Assert.That(GameRealmGate.MayPass(eRealm.Albion, eRealm.Albion, eDoorState.Closed, borderKeep: true), Is.True);
        Assert.That(GameRealmGate.MayPass(eRealm.Hibernia, eRealm.Albion, eDoorState.Closed, borderKeep: true), Is.False);
        Assert.That(GameRealmGate.MayPass(eRealm.Hibernia, eRealm.Albion, eDoorState.Open, borderKeep: true), Is.True,
            "slipping through while its realm has it open");
        Gate door = NewGate(eRealm.Albion, doorId: BorderKeepId);
        Assert.That(door.IsBorderKeep, Is.True);
        Assert.That(door.CanBeOpenedViaInteraction, Is.False, "registered on the navmesh: shut for everyone but its realm");
        Assert.That(door.MayOpen(NewBot(eRealm.Albion)), Is.True);
        Assert.That(door.MayOpen(NewBot(eRealm.Midgard)), Is.False);
    }

    [Test]
    public void NothingDamagesABorderKeepDoor()
    {
        Gate door = NewGate(eRealm.Albion, doorId: BorderKeepId);
        Assert.That(door.IsAttackableDoor, Is.False);
        Assert.That(door.Health, Is.Zero, "no health, as a postern: every attack is refused");
        Assert.That(door.IsBroken, Is.False, "and it is never broken open");
        foreach (eRealm actor in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            Assert.That(GameRealmGate.MayBreak(actor, eRealm.Albion, alive: true, borderKeep: true), Is.False, actor.ToString());
    }

    [Test]
    public void BotsTreatAMilegateAsADoorToBreachWhenTheyFightIt()
    {
        Assert.That(AutonomousRvrObjectiveState.ForTarget(NewGate(eRealm.Albion)), Is.EqualTo(eAutonomousRvrPhase.BreachingDoor));
    }

    [Test]
    public void TheDoorObjectPacketCarriesTheOwnersRealm()
    {
        // Client: realm = (flags >> 4) & 3 (live test 2026-11-06 read 2 for every gate: realm 6 rows, 6 & 3).
        static int ClientRealm(GameObject obj, bool classic165) => (((GameDoorBase.PacketRealm(obj, classic165) & 3) << 4) >> 4) & 3;
        Assert.That(ClientRealm(NewGate(eRealm.Albion), true), Is.EqualTo(1), "Hadrian's milegate: Albion");
        Assert.That(ClientRealm(NewGate(eRealm.Hibernia), true), Is.EqualTo(3), "Emain's milegate: Hibernia");
        Assert.That(ClientRealm(NewGate(eRealm.Midgard, doorId: BorderKeepId), true), Is.EqualTo(2), "a border keep door: its realm");
        Assert.That(ClientRealm(NewKeepDoor(3), true), Is.EqualTo(3), "a keep door: its keep's realm");
        var plain = new GameDoor { Realm = eRealm.Door };
        Assert.That(ClientRealm(plain, true), Is.Zero, "a town door belongs to no realm");
        Assert.That(ClientRealm(plain, false), Is.EqualTo(2), "normal mode unchanged");
        Assert.That(ClientRealm(NewBot(eRealm.Midgard), true), Is.EqualTo(2), "other objects unchanged");
    }
}
