using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Publishes persistent friendly autonomous bots through the client's scalable
/// map-marker channel. It never alters group membership and never reveals an
/// opposing-realm bot, including in the frontiers.
///
/// Every bot of the player's realm is sent, wherever it is (owner 2026-11-05: the
/// green dots on any zone picked from the map list and all over the realm maps,
/// not only in the zone the player is in). Built for thousands of bots a realm
/// at little cost: one poll a second walks the bot list once per player; a bot on
/// the player's own map (their zone outdoors, the region on an interior map) goes
/// out at most every 4 s and any other at most every 12 s, each on its own poll by
/// its id so the packets spread evenly over the seconds (2,000 moving bots: about
/// 170 small packets a second), and only when it moved. A new bot, a region change
/// and a bot that leaves go out at once. One dictionary per player, kept in place.
/// </summary>
public static class AutonomousBotMapMarkers
{
    private const uint BotMarkerNamespace = 0x80000000;
    private const int PollMilliseconds = 1000;
    /// <summary>Polls between two updates of a bot on the player's own map (4 s, as before 2026-11-05).</summary>
    public const int NearPolls = 4;
    /// <summary>Polls between two updates of any other bot of the realm (other zones' maps, the realm maps).</summary>
    public const int FarPolls = 12;
    private const int MinimumMovementBeforeUpdate = 192;
    private const int InteriorRefreshMilliseconds = 12000;
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBotMapMarkers));
    private static readonly ConcurrentDictionary<GamePlayer, Dictionary<uint, MarkerState>> VisibleByPlayer = new();
    private static Timer _timer;
    private static int _polling;
    private static long _poll;

    private readonly record struct MarkerState(ushort Region, int X, int Y, int Z, long SentAtTick, long SeenPoll);

    public static ushort ClientMapRegion(ushort actualRegionId, ushort regionSkin) =>
        regionSkin != 0 ? regionSkin : actualRegionId;

    public readonly record struct ClientMapPosition(ushort MapId, int X, int Y, int Z);

    /// <summary>
    /// Minotaur-relic map packets always use the actor's actual region and
    /// region/global coordinates. They are not ordinary object-create packets:
    /// converting interiors to ZoneSkinID and zone-local X/Y displaces markers.
    /// Client 1.127 also needs tools/patch_bot_map_client.py: its stock zone
    /// lookup omits the map crop origin when checking cropped-map bounds.
    /// Preserve Z so the client's unchanged area selector handles each floor.
    /// </summary>
    public static ClientMapPosition ResolveClientMapPosition(
        bool interior,
        ushort actualRegionId,
        ushort regionSkin,
        ushort zoneSkinId,
        int zoneXOffset,
        int zoneYOffset,
        int x,
        int y,
        int z)
    {
        return new ClientMapPosition(actualRegionId, x, y, z);
    }

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        _timer = new Timer(Poll, null, PollMilliseconds, PollMilliseconds);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        _timer = null;
        VisibleByPlayer.Clear();
    }

    /// <summary>Every friendly autonomous bot of the player's realm, wherever it is; the client draws those on the map
    /// being looked at. Enemy bots never.</summary>
    internal static bool IsVisibleTo(GamePlayer player, GameBot bot) =>
        player?.ObjectState == GameObject.eObjectState.Active &&
        bot?.ObjectState == GameObject.eObjectState.Active &&
        bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper &&
        player.Realm == bot.Realm;

    private static bool IsInteriorMap(GameLiving living) =>
        living?.CurrentRegion?.IsCapitalCity == true ||
        living?.CurrentRegion?.IsDungeon == true ||
        living?.CurrentZone?.IsDungeon == true;

    public static bool ShouldRefreshStationaryInterior(bool interior, long elapsedMilliseconds) =>
        interior && elapsedMilliseconds >= InteriorRefreshMilliseconds;

    /// <summary>
    /// Whether a bot's marker may go out again on this poll: every <see cref="NearPolls"/> polls for a bot on the
    /// player's own map, every <see cref="FarPolls"/> for the rest, each bot on its own poll by its id, so the updates
    /// spread evenly instead of all leaving together.
    /// </summary>
    public static bool IsMarkerTurn(uint markerId, long poll, bool near) =>
        (markerId + poll) % (near ? NearPolls : FarPolls) == 0;

    /// <summary>Whether a bot moved enough to be sent again: 192 units across, or - on an interior map, whose floors a
    /// stair can change without moving across - any change of height.</summary>
    public static bool HasMoved(int oldX, int oldY, int oldZ, int x, int y, int z, bool floors)
    {
        if (floors && HasChangedFloorHeight(oldZ, z))
            return true;

        long dx = oldX - (long)x;
        long dy = oldY - (long)y;
        long threshold = MinimumMovementBeforeUpdate;
        return dx * dx + dy * dy >= threshold * threshold;
    }

    private static uint MarkerId(GameBot bot) =>
        BotMarkerNamespace | ((uint)bot.DatabaseID & 0x7FFFFFFF);

    private static void Poll(object state)
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0)
            return;

        try
        {
            long poll = Interlocked.Increment(ref _poll);
            GamePlayer[] players = ClientService.Instance.GetPlayers()
                .Where(player => player?.ObjectState == GameObject.eObjectState.Active)
                .ToArray();
            GameBot[] bots = AutonomousBotRegistry.Snapshot();
            HashSet<GamePlayer> activePlayers = players.ToHashSet();

            foreach (GamePlayer stalePlayer in VisibleByPlayer.Keys.Where(player => !activePlayers.Contains(player)))
                VisibleByPlayer.TryRemove(stalePlayer, out _);

            foreach (GamePlayer player in players)
                UpdatePlayer(player, bots, poll);
        }
        catch (Exception ex)
        {
            Log.Error("Unable to publish autonomous bot map markers.", ex);
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private static void UpdatePlayer(GamePlayer player, GameBot[] bots, long poll)
    {
        Dictionary<uint, MarkerState> shown = VisibleByPlayer.GetOrAdd(player, _ => new Dictionary<uint, MarkerState>());
        long now = GameLoop.GameLoopTime;
        bool interior = IsInteriorMap(player);
        ushort playerRegion = player.CurrentRegionID;
        int playerZone = player.CurrentZone?.ID ?? -1;

        foreach (GameBot bot in bots)
        {
            if (!IsVisibleTo(player, bot))
                continue;

            uint markerId = MarkerId(bot);
            Zone zone = bot.CurrentZone;
            // the player's own map: their zone outdoors, the whole region on an interior map (a capital, a dungeon)
            bool near = interior ? bot.CurrentRegionID == playerRegion : zone != null && zone.ID == playerZone;
            ClientMapPosition at = ResolveClientMapPosition(
                interior,
                bot.CurrentRegionID,
                bot.CurrentRegion?.Skin ?? 0,
                zone?.ZoneSkinID ?? 0,
                zone?.XOffset ?? 0,
                zone?.YOffset ?? 0,
                bot.X,
                bot.Y,
                bot.Z);
            bool known = shown.TryGetValue(markerId, out MarkerState old);
            bool send = !known || old.Region != at.MapId ||
                        IsMarkerTurn(markerId, poll, near) &&
                        (HasMoved(old.X, old.Y, old.Z, at.X, at.Y, at.Z, near && interior) ||
                         ShouldRefreshStationaryInterior(near && interior, now - old.SentAtTick));
            if (send)
            {
                player.Out.SendMinotaurRelicMapUpdate(markerId, at.MapId, at.X, at.Y, at.Z);
                shown[markerId] = new MarkerState(at.MapId, at.X, at.Y, at.Z, now, poll);
            }
            else
            {
                shown[markerId] = old with { SeenPoll = poll };
            }
        }

        // the bots that left (dead and gone, logged off, another realm): off the map at once
        List<uint> gone = null;
        foreach (KeyValuePair<uint, MarkerState> entry in shown)
            if (entry.Value.SeenPoll != poll)
                (gone ??= []).Add(entry.Key);

        if (gone == null)
            return;

        foreach (uint markerId in gone)
        {
            shown.Remove(markerId);
            player.Out.SendMinotaurRelicMapRemove(markerId);
        }
    }

    public static bool HasChangedFloorHeight(int previousZ, int currentZ) => previousZ != currentZ;
}
