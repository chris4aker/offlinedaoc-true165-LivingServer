using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_AutonomousBotMapMarkers
{
    [Test]
    public void VerticalFloorTransitionsRefreshEvenWithoutHorizontalMovement()
    {
        Assert.That(AutonomousBotMapMarkers.HasChangedFloorHeight(16202, 16000), Is.True);
        Assert.That(AutonomousBotMapMarkers.HasChangedFloorHeight(16202, 16202), Is.False);
    }

    // Owner 2026-11-05: every bot of the realm goes to the player (the green dots on any zone's map and the realm maps),
    // and it has to stay light with 1,000-2,000 bots a realm.
    [Test]
    public void BotsInOtherZonesSpreadEvenlyOverTheirTwelvePolls()
    {
        var perPoll = new int[AutonomousBotMapMarkers.FarPolls];
        for (uint id = 0; id < 1200; id++)
        {
            uint marker = 0x80000000 | (185000 + id);
            int turns = 0;
            for (int poll = 1; poll <= AutonomousBotMapMarkers.FarPolls; poll++)
            {
                if (AutonomousBotMapMarkers.IsMarkerTurn(marker, poll, near: false))
                {
                    turns++;
                    perPoll[poll - 1]++;
                }
            }

            Assert.That(turns, Is.EqualTo(1), $"bot {id}: one update in 12 s");
        }

        Assert.That(perPoll, Is.All.EqualTo(100), "100 of 1,200 bots on every poll, never all at once");
    }

    [Test]
    public void BotsOnThePlayersOwnMapKeepTheirFourSecondUpdates()
    {
        uint marker = 0x80000000 | 185054;
        int turns = Enumerable.Range(1, 12).Count(poll => AutonomousBotMapMarkers.IsMarkerTurn(marker, poll, near: true));
        Assert.That(turns, Is.EqualTo(3));
    }

    [Test]
    public void OnlyMovementSendsABotAgain()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotMapMarkers.HasMoved(1000, 1000, 50, 1100, 1100, 50, false), Is.False, "141 units");
            Assert.That(AutonomousBotMapMarkers.HasMoved(1000, 1000, 50, 1192, 1000, 50, false), Is.True, "192 units");
            Assert.That(AutonomousBotMapMarkers.HasMoved(1000, 1000, 50, 1000, 1000, 300, false), Is.False, "height outdoors");
            Assert.That(AutonomousBotMapMarkers.HasMoved(1000, 1000, 50, 1000, 1000, 300, true), Is.True, "another floor inside");
        });
    }

    [Test]
    public void StationaryInteriorMarkersReceiveBoundedRefreshes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotMapMarkers.ShouldRefreshStationaryInterior(true, 11_999), Is.False);
            Assert.That(AutonomousBotMapMarkers.ShouldRefreshStationaryInterior(true, 12_000), Is.True);
            Assert.That(AutonomousBotMapMarkers.ShouldRefreshStationaryInterior(false, 60_000), Is.False);
        });
    }

    [Test]
    public void InteriorMarkersUseTheClientVisibleRegionSkin()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotMapMarkers.ClientMapRegion(20001, 100), Is.EqualTo(100));
            Assert.That(AutonomousBotMapMarkers.ClientMapRegion(100, 0), Is.EqualTo(100));
        });
    }

    [Test]
    public void DungeonMarkersUseActualRegionAndGlobalCoordinatesOnEveryFloor()
    {
        var marker = AutonomousBotMapMarkers.ResolveClientMapPosition(
            true, 129, 129, 129, 8192, 8192, 33486, 32889, 16202);
        Assert.Multiple(() =>
        {
            Assert.That(marker.MapId, Is.EqualTo(129));
            Assert.That(marker.X, Is.EqualTo(33486));
            Assert.That(marker.Y, Is.EqualTo(32889));
            Assert.That(marker.Z, Is.EqualTo(16202));
        });
    }

    [Test]
    public void CapitalMarkersUseActualRegionAndGlobalCoordinates()
    {
        var jordheim = AutonomousBotMapMarkers.ResolveClientMapPosition(
            true, 101, 101, 120, 8192, 8192, 30000, 28000, 9000);
        Assert.Multiple(() =>
        {
            Assert.That(jordheim.MapId, Is.EqualTo(101));
            Assert.That(jordheim.X, Is.EqualTo(30000));
            Assert.That(jordheim.Y, Is.EqualTo(28000));
        });
    }

    [Test]
    public void OutdoorMarkersRetainExistingRegionGlobalCoordinateContract()
    {
        var marker = AutonomousBotMapMarkers.ResolveClientMapPosition(
            false, 100, 100, 101, 655360, 655360, 712480, 783994, 4798);
        Assert.Multiple(() =>
        {
            Assert.That(marker.MapId, Is.EqualTo(100));
            Assert.That(marker.X, Is.EqualTo(712480));
            Assert.That(marker.Y, Is.EqualTo(783994));
        });
    }
}
