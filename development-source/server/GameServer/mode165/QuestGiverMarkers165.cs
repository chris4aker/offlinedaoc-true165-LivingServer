using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// "Quest available here" map markers for the whole realm (owner 2026-11-05, reset-day S40 / C18: in Lough Derg,
    /// opening the Shannon Estuary map should show the quests there you can take; before, a yellow marker existed only
    /// for NPCs near you). Every NPC of the player's realm (and of no realm) that would show this player the "quest
    /// available" icon right now gets one map marker, wherever it stands: the same check as the icon over its head
    /// (level, class, race, done, prerequisites). Sent at login and refreshed every 30 s, so a level-up, a finished quest
    /// or a new class shows within half a minute. Markers use their own id range, 0xFFFC0000 + giver number, which the
    /// new client draws as the yellow "!" on any zone's map and counts per zone on the realm map. Fixed spawns only (an
    /// NPC not in the world at the moment, or one that wanders or comes only at times, is shown when it is there).
    /// </summary>
    public static class QuestGiverMarkers165
    {
        public const uint MarkerBase = 0xFFFC0000;
        private const int RefreshMilliseconds = 30_000;
        private const long GiverListMilliseconds = 600_000;

        private static readonly Logger Log = LoggerManager.Create(typeof(QuestGiverMarkers165));
        private static readonly ConcurrentDictionary<GameNPC, uint> GiverIds = new();
        private static readonly ConcurrentDictionary<GamePlayer, HashSet<uint>> Shown = new();
        private static GameNPC[] _givers = [];
        private static long _giversAt = long.MinValue;
        private static int _nextId;
        private static Timer _timer;

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Dispose();
            if (Classic165Mode.IsOn)
                _timer = new Timer(Refresh, null, 15_000, RefreshMilliseconds);
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, OnEntered);
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Dispose();
            _timer = null;
            GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, OnEntered);
            Shown.Clear();
        }

        private static void OnEntered(DOLEvent e, object sender, EventArgs args)
        {
            if (Classic165Mode.IsOn && sender is GamePlayer player)
            {
                Shown.TryRemove(player, out _);   // a new session: the client has none of them yet
                ThreadPool.QueueUserWorkItem(_ => Update(player));
            }
        }

        private static void Refresh(object state)
        {
            try
            {
                foreach (GamePlayer player in ClientService.Instance.GetPlayers())
                    Update(player);
                foreach (GamePlayer gone in Shown.Keys)
                    if (gone.ObjectState != GameObject.eObjectState.Active)
                        Shown.TryRemove(gone, out _);
            }
            catch (Exception ex)
            {
                Log.Error("Quest giver markers: refresh failed", ex);
            }
        }

        /// <summary>Every NPC in the world that gives a quest (scripted or data), looked up again every 10 minutes.</summary>
        private static GameNPC[] Givers()
        {
            long now = Environment.TickCount64;
            if (now - _giversAt < GiverListMilliseconds)
                return _givers;
            var list = new List<GameNPC>();
            foreach (Region region in WorldMgr.GetAllRegions())
                foreach (GameNPC npc in region.Objects.OfType<GameNPC>())
                    if (npc is not GameBot && npc.ObjectState == GameObject.eObjectState.Active && (npc.QuestListToGive.Count > 0 || npc.DataQuestList.Count > 0))
                        list.Add(npc);
            _givers = list.ToArray();
            _giversAt = now;
            return _givers;
        }

        private static uint IdOf(GameNPC npc) => GiverIds.GetOrAdd(npc, _ => MarkerBase + (uint)(Interlocked.Increment(ref _nextId) & 0xFFFF));

        private static void Update(GamePlayer player)
        {
            if (player?.ObjectState != GameObject.eObjectState.Active || player.Out == null || player.Client?.ClientState != GameClient.eClientState.Playing)
                return;
            var now = new HashSet<uint>();
            foreach (GameNPC npc in Givers())
            {
                if (npc.ObjectState != GameObject.eObjectState.Active || npc.Realm != player.Realm && npc.Realm != eRealm.None)
                    continue;
                bool available;
                try { available = npc.CanShowOneQuest(player); }
                catch (Exception) { continue; }
                if (!available)
                    continue;
                uint id = IdOf(npc);
                now.Add(id);
            }
            Shown.TryGetValue(player, out HashSet<uint> before);
            // only what changed is sent: the client keeps its markers until removed
            foreach (GameNPC npc in Givers())
                if (GiverIds.TryGetValue(npc, out uint id) && now.Contains(id) && before?.Contains(id) != true)
                    player.Out.SendMinotaurRelicMapUpdate(id, npc.CurrentRegionID, npc.X, npc.Y, npc.Z);
            if (before != null)
                foreach (uint gone in before.Where(id => !now.Contains(id)))
                    player.Out.SendMinotaurRelicMapRemove(gone);
            Shown[player] = now;
        }
    }
}
