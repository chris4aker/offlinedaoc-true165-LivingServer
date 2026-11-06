using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>True 1.65 Mode (owner 2026-11-06): the keep's own realm opens its keep doors and posterns by hand (DoorRequest);
/// enemies are refused and have to break the door.</summary>
[TestFixture, NonParallelizable]
public class UT_KeepDoorHand165 : RealmGateFixture
{
    [Test]
    public void KeepDoorOpensByHandForItsRealmOnly()
    {
        Assert.That(GameKeepDoor.MayOpenByHand(eRealm.Midgard, eRealm.Midgard, broken: false), Is.True);
        Assert.That(GameKeepDoor.MayOpenByHand(eRealm.Albion, eRealm.Midgard, broken: false), Is.False);
        Assert.That(GameKeepDoor.MayOpenByHand(eRealm.Albion, eRealm.Midgard, broken: true), Is.True, "a broken door already stands open");
        Assert.That(GameKeepDoor.MayOpenByHand(eRealm.None, eRealm.None, broken: false), Is.False, "nobody owns an unclaimed keep's door");
        Assert.That(GameKeepDoor.MayOpenByHand(eRealm.Albion, eRealm.None, broken: false), Is.False, "a neutral battleground keep's door is broken, not opened");
    }

    [Test]
    public void FriendlyOpeningIsShort()
    {
        Assert.That(GameKeepDoor.FriendlyOpenMilliseconds, Is.EqualTo(5_000), "as long as an ordinary door stays open");
    }
}
