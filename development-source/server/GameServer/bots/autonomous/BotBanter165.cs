using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Live side of the data-driven banter (owner 2026-11-10). Reads banter165/*.json once (server folder), works out
    /// what a speaking bot is doing, and hands the chat code a fitting line. Every entry point returns null when no
    /// line fits, and the caller then says today's built-in line, so a missing or empty folder changes nothing.
    /// Nothing here runs on an AI tick: only when the existing, rate-limited chat triggers have already decided to talk.
    /// </summary>
    public static class BotBanter165
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly object LoadSync = new();
        private static volatile BanterLibrary165.BanterSet _set;

        private static readonly object RecentSync = new();
        private static readonly Queue<int> RecentQueue = new();
        private static readonly HashSet<int> RecentIds = new();
        private const int RecentMemory = 160;   // a line is not repeated until 160 others were said

        // when each bot last gained a level (game loop tick), from AutonomousBotLifecycle.LevelChanged (owner 2026-11-10)
        private static readonly ConcurrentDictionary<long, long> LevelUpAt = new();
        private static readonly ConcurrentDictionary<string, long> LastGroupReply = new();
        private static readonly ConcurrentDictionary<eRealm, long> LastHorn = new();
        private static readonly ConcurrentDictionary<string, string> RaidNames = new(StringComparer.Ordinal);
        private static long _raidNamesBuilt;

        /// <summary>A started conversation: its situation and the event names repliers may reuse.</summary>
        public sealed class Topic
        {
            public string Situation;
            public Dictionary<string, string> Shared = new(StringComparer.Ordinal);
        }

        #region Loading

        public static BanterLibrary165.BanterSet Current
        {
            get
            {
                BanterLibrary165.BanterSet set = _set;
                if (set != null)
                    return set;
                lock (LoadSync)
                    return _set ??= LoadNow();
            }
        }

        /// <summary>runtime\server\banter165 (next to the server's config root); null without a running server.</summary>
        public static string FolderPath
        {
            get
            {
                try
                {
                    string root = GameServer.Instance?.Configuration?.RootDirectory;
                    return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, BanterLibrary165.FolderName);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private static BanterLibrary165.BanterSet LoadNow()
        {
            string folder = FolderPath;
            try
            {
                BanterLibrary165.BanterSet set = BanterLibrary165.LoadFolder(folder);
                if (set.Files > 0 || set.Errors.Count > 0)
                    Log.Info($"BANTER165 loaded {set.Count} lines from {set.Files} files in {folder}; {set.Errors.Count} problems skipped");
                foreach (string error in set.Errors.Take(25))
                    Log.Warn($"BANTER165 {error}");
                return set;
            }
            catch (Exception exception)
            {
                Log.Error("BANTER165 could not load banter lines", exception);
                return new BanterLibrary165.BanterSet();
            }
        }

        /// <summary>Re-reads the folder (GM /banter reload).</summary>
        public static string Reload()
        {
            BanterLibrary165.BanterSet set = LoadNow();
            lock (LoadSync)
                _set = set;
            return $"Banter: {set.Count} lines from {set.Files} files, {set.Errors.Count} problems ({FolderPath ?? "no folder"}).";
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            _ = Current;
            AutonomousBotLifecycle.LevelChanged -= NoteLevelUp;
            AutonomousBotLifecycle.LevelChanged += NoteLevelUp;
        }

        // Records the moment of a level-up so "levelup" lines are said soon after it, not hours later (owner 2026-11-10).
        private static void NoteLevelUp(long botId, eRealm realm, int level)
        {
            long now = GameLoop.GameLoopTime;
            LevelUpAt[botId] = now;
            if (LevelUpAt.Count > 4_096)
                foreach (KeyValuePair<long, long> entry in LevelUpAt)
                    if (!BanterLibrary165.IsRecentLevelUp(entry.Value, now))
                        LevelUpAt.TryRemove(entry.Key, out _);
        }

        #endregion

        #region What a bot is doing

        private static readonly ushort[] Capitals = [10, 101, 201];   // Camelot, Jordheim, Tir na Nog

        private static string Clean(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('.', '!', '?');

        private static bool Has(string text, params string[] words) =>
            !string.IsNullOrEmpty(text) && words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));

        /// <summary>Live facts and placeholder values for one bot. Called only when it is about to speak.</summary>
        public static (BanterLibrary165.Facts Facts, Dictionary<string, string> Values) Read(GameBot bot, AutonomousBotChat.Context context = null)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (bot == null)
                return (new BanterLibrary165.Facts(), values);
            long now = GameLoop.GameLoopTime;
            OfflineWorldBotRecord record = bot.PersistentRecord;
            string activity = record?.Activity ?? string.Empty;
            string goal = record?.CurrentGoal ?? string.Empty;
            values["bot"] = bot.Name;
            values["realm"] = BanterLibrary165.RealmName(bot.Realm);
            values["level"] = bot.Level.ToString();
            values["class"] = bot.ClassName;
            values["zone"] = bot.CurrentZone?.Description ?? string.Empty;
            values["time"] = bot.CurrentRegion?.IsNightTime == true ? "night" : "day";
            string guild = AutonomousBotGuilds.GuildNameFor(bot);
            if (!string.IsNullOrWhiteSpace(guild))
                values["guild"] = guild;
            string target = Clean(record?.TargetName) ?? Clean(context?.MonsterName);
            string destination = Clean(record?.TravelDestination);
            string item = Clean(context?.ItemName);
            if (target != null) values["target"] = target;
            if (destination != null) values["destination"] = destination;
            if (item != null) values["item"] = item;

            string battleground = null;
            if (BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID) && BattlegroundBrackets.ForRegion(bot.CurrentRegionID) is { } bracket)
            {
                battleground = bracket.Name;
                values["bg"] = bracket.Name;
            }

            string raidState = null;
            AutonomousRealmRaid.View view = AutonomousRealmRaid.GetView(bot.Group);
            if (view != null)
            {
                string state = view.State ?? string.Empty;
                raidState = state.StartsWith("RAID", StringComparison.Ordinal) || Has(state, "fighting", "dragon landing") ? "boss"
                    : state.StartsWith("Traveling", StringComparison.Ordinal) ? "march" : "forming";
                if (RaidName(view.EventId) is { } raid)
                    values["raid"] = raid;
                if (raidState == "boss" && !string.IsNullOrWhiteSpace(view.Camp?.MonsterName))
                    values["boss"] = view.Camp.MonsterName;
            }

            string rvrRole = null;
            eRealm knownEnemy = eRealm.None;
            string force = bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";
            // the cheap membership test first; the force map is built only for a bot that is in a siege
            if (AutonomousRvrEventLayer.IsEventForce(force, now) && AutonomousRvrEventLayer.ForceTargets().TryGetValue(force, out string targetId))
            {
                int keepId = BanterLibrary165.KeepIdOf(targetId);
                DOL.GS.Keeps.AbstractGameKeep keep = keepId >= 0 ? GameServer.KeepManager?.GetKeepByID(keepId) : null;
                if (keep != null)
                {
                    values["keep"] = AutonomousRvrEventLayer.Place(keep.Name);
                    rvrRole = keep.IsRelic ? "relic" : keep.Realm == bot.Realm ? "defend" : "siege";
                }
                else
                    rvrRole = targetId.Contains("relic", StringComparison.Ordinal) ? "relic" : "siege";
                // the event's real realms first (a defender faces the attacker), then the keep's owner (owner 2026-11-10)
                (eRealm attacker, eRealm defender) = AutonomousRvrEventLayer.EventRealms(targetId);
                knownEnemy = BanterLibrary165.EnemyOf(bot.Realm, attacker, defender);
                if (knownEnemy == eRealm.None && keep != null && keep.Realm != bot.Realm)
                    knownEnemy = keep.Realm;
            }
            values["enemyrealm"] = BanterLibrary165.EnemyRealmName(bot.Realm, knownEnemy, Random.Shared);

            bool leveledUp = LevelUpAt.TryGetValue(bot.DatabaseID, out long levelUpTick) && BanterLibrary165.IsRecentLevelUp(levelUpTick, now);
            bool recentDeath = bot.LastDeathTick165 > 0 && now - bot.LastDeathTick165 < 5 * 60_000;
            (ushort Region, int X, int Y) spot = bot.LastDeathSpot;

            var facts = new BanterLibrary165.Facts(
                Battleground: battleground,
                RaidState: raidState,
                RvrRole: rvrRole,
                Frontier: battleground == null && bot.CurrentZone?.IsRvR == true,
                Dungeon: bot.CurrentZone?.IsDungeon == true || bot.CurrentRegion?.IsDungeon == true,
                Town: Array.IndexOf(Capitals, bot.CurrentRegionID) >= 0 ||
                      Has(activity, "merchant", "vendor", "shop", "trainer", "training", "repair", "healer", "vault", "bank", "buying", "selling"),
                Crafting: Has(activity, "craft") || Has(goal, "craft"),
                Trading: item != null && Has(activity, "exchange", "merchant", "vendor", "selling", "shop") || Has(activity, "Realm Exchange"),
                Traveling: destination != null && Has(activity, "Walking to", "Approaching", "Travel", "Heading"),
                HasTarget: target != null,
                GroupSize: Math.Max(1, (int)(bot.Group?.MemberCount ?? 1)),
                LeveledUp: leveledUp,
                RecentDeath: recentDeath,
                ResurrectedInPlace: recentDeath && BanterLibrary165.WasResurrectedInPlace(spot.Region, spot.X, spot.Y, bot.CurrentRegionID, bot.X, bot.Y));
            return (facts, values);
        }

        /// <summary>A great hunt's name from its id; the list is read again at most once a minute on a miss.</summary>
        public static string RaidName(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (RaidNames.TryGetValue(id, out string name))
                return name;
            long now = GameLoop.GameLoopTime;
            if (_raidNamesBuilt != 0 && now - _raidNamesBuilt < 60_000)
                return null;
            _raidNamesBuilt = now;
            try
            {
                foreach (AutonomousRealmRaid.Summary summary in AutonomousRealmRaid.Snapshot())
                    RaidNames[summary.Id] = summary.Name;
            }
            catch (Exception)
            {
                return null;
            }
            return RaidNames.TryGetValue(id, out name) ? name : null;
        }

        #endregion

        #region Picking

        private static bool IsRecent(int id)
        {
            lock (RecentSync)
                return RecentIds.Contains(id);
        }

        private static void Remember(int id)
        {
            lock (RecentSync)
            {
                if (!RecentIds.Add(id))
                    return;
                RecentQueue.Enqueue(id);
                while (RecentQueue.Count > RecentMemory)
                    RecentIds.Remove(RecentQueue.Dequeue());
            }
        }

        /// <summary>
        /// Picks and fills a line. Where today's code already has lines for the matched situation, the library is
        /// used only part of the time (BanterLibrary165.UseLibraryChance) so the old lines stay in the mix.
        /// </summary>
        private static string Pick(IReadOnlyList<string> keys, string channel, GameBot speaker, Dictionary<string, string> values,
            bool? reply, out string situation) => Pick(keys, channel, speaker, values, reply, null, out situation, out _);

        /// <summary>The same, with the chosen line; mustTest keeps only lines whose "only" tests that value.</summary>
        private static string Pick(IReadOnlyList<string> keys, string channel, GameBot speaker, Dictionary<string, string> values,
            bool? reply, string mustTest, out string situation, out BanterLibrary165.Line line)
        {
            situation = null;
            line = null;
            BanterLibrary165.BanterSet set = Current;
            if (set.Count == 0 || speaker == null)
                return null;
            var query = new BanterLibrary165.Query(keys, channel, speaker.Realm, speaker.Level, speaker.ClassName, reply, values, mustTest);
            int candidates = BanterLibrary165.CountCandidates(set, query, out string matched);
            if (candidates == 0)
                return null;
            if (BanterLibrary165.BuiltInCovers(matched) && Random.Shared.NextDouble() >= BanterLibrary165.UseLibraryChance(candidates))
                return null;
            line = BanterLibrary165.Choose(set, query, Random.Shared, IsRecent, out situation);
            if (line == null)
                return null;
            Remember(line.Id);
            string text = BanterLibrary165.Fill(line.Text, values);
            if (string.IsNullOrWhiteSpace(text))
                return null;
            FeatureCounters165.Add(CounterName(channel, reply));
            return text;
        }

        /// <summary>FEATURE_COUNTERS_165 key of a library line: banter_realm / _guild / _local (say) / _party (group),
        /// with _reply for answers (to another bot or to a player).</summary>
        public static string CounterName(string channel, bool? reply)
        {
            string name = channel switch
            {
                "realm" => "banter_realm",
                "guild" => "banter_guild",
                "say" => "banter_local",
                "group" => "banter_party",
                _ => "banter_other",
            };
            return reply == true ? name + "_reply" : name;
        }

        /// <summary>An opening line for a bot that has decided to speak (realm, say or guild channel), or null.</summary>
        public static string TryOpening(GameBot bot, AutonomousBotChat.Context context, string channel, out Topic topic)
        {
            topic = null;
            if (bot == null || Current.Count == 0)
                return null;
            (BanterLibrary165.Facts facts, Dictionary<string, string> values) = Read(bot, context);
            List<string> keys = BanterLibrary165.ResolveAmbient(facts, Random.Shared);
            string line = Pick(keys, channel, bot, values, false, null, out string situation, out BanterLibrary165.Line chosen);
            if (line == null)
                return null;
            if (situation == "levelup")
                LevelUpAt.TryRemove(bot.DatabaseID, out _);   // one level-up line per level
            topic = new Topic { Situation = situation };
            if (chosen?.Invites != null)
                topic.Shared["thread"] = chosen.Invites;   // a boast is answered as a boast
            foreach (string shared in new[] { "keep", "raid", "boss", "bg", "enemyrealm", "target", "destination", "item" })
                if (values.TryGetValue(shared, out string value))
                    topic.Shared[shared] = value;
            return line;
        }

        /// <summary>Another bot's answer to an opening on the same topic (kind "reply" lines), or null.</summary>
        public static string TryReply(GameBot responder, Topic topic, string channel)
        {
            if (responder == null || topic?.Situation == null || Current.Count == 0)
                return null;
            Dictionary<string, string> values = Read(responder).Values;
            // the opener's topic (enemyrealm, keep, target...) wins over the responder's own values (owner 2026-11-10)
            foreach ((string key, string value) in topic.Shared)
                values[key] = value;
            // the opener asked for something (a boast, a joke): most of the time the answer is one written for that
            if (topic.Shared.ContainsKey("thread") && Random.Shared.NextDouble() < BanterLibrary165.ThreadReplyChance &&
                Pick([topic.Situation], channel, responder, values, true, "thread", out _, out _) is { } threaded)
                return threaded;
            values.Remove("thread");
            return Pick([topic.Situation], channel, responder, values, true, out _);
        }

        /// <summary>The opener's short closing line after the replies, or null.</summary>
        public static string TryClosing(GameBot starter, string channel) =>
            starter == null || Current.Count == 0 ? null : Pick(["chat_close"], channel, starter, Read(starter).Values, null, out _);

        /// <summary>A bot's answer to a player's message (realm, guild, say or group channel), or null.</summary>
        public static string TryPlayerReply(GameBot responder, GamePlayer player, string message, string channel, bool directed)
        {
            if (responder == null || player == null || string.IsNullOrWhiteSpace(message) || Current.Count == 0)
                return null;
            Dictionary<string, string> values = Read(responder).Values;
            values["player"] = player.Name;
            // asked about a place by name, a bot that has something to say about that place says it
            if (BanterLibrary165.PlaceNamed(message, ZoneNames()) is { } place && BanterLibrary165.AsksAboutPlace(message, directed))
            {
                var about = new Dictionary<string, string>(values, StringComparer.Ordinal) { ["zone"] = place };
                if (Pick(BanterLibrary165.PlaceTalk, channel, responder, about, false, "zone", out _, out _) is { } told)
                    return told;
            }
            return Pick(BanterLibrary165.ClassifyPlayerMessage(message, directed), channel, responder, values, true, out _);
        }

        private static string[] _zoneNames;

        /// <summary>Every zone's name, read once (the world's zones do not change while the server runs).</summary>
        private static string[] ZoneNames()
        {
            if (_zoneNames != null)
                return _zoneNames;
            try
            {
                _zoneNames = WorldMgr.Zones.Values.Select(zone => zone?.Description).Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception)
            {
                return [];
            }
            return _zoneNames;
        }

        /// <summary>One line for a fixed situation (e.g. guild_join), or null.</summary>
        public static string TryLine(GameBot bot, string situation, string channel) =>
            bot == null || Current.Count == 0 ? null : Pick([situation], channel, bot, Read(bot).Values, null, out _);

        #endregion

        #region Event follow-ups (herald notices, Horn of Rallying, party chat)

        private static bool CanTalk(GameBot bot) =>
            bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsAlive: true, InCombat: false, IsCasting: false } &&
            bot.ObjectState == GameObject.eObjectState.Active && !CompanionChat165.IsCompanion(bot);

        /// <summary>
        /// A realm herald notice was just broadcast (RealmEventNotices, at most one a minute per realm): up to two bots,
        /// those in that event first, follow it on the realm channel with a line for its situation.
        /// </summary>
        public static void FollowNotice(string eventId, eRealm realm, string text, string heraldName)
        {
            string situation = BanterLibrary165.ClassifyNotice(eventId, text);
            if (situation == null || Current.CountFor(situation) == 0 || Random.Shared.NextDouble() > 0.6)
                return;
            var shared = new Dictionary<string, string>(StringComparer.Ordinal);
            int keepId = BanterLibrary165.KeepIdOf(eventId);
            DOL.GS.Keeps.AbstractGameKeep keep = keepId >= 0 ? GameServer.KeepManager?.GetKeepByID(keepId) : null;
            bool rvr = eventId?.StartsWith("rvr-", StringComparison.Ordinal) == true;
            if (keep != null)
                shared["keep"] = AutonomousRvrEventLayer.Place(keep.Name);
            else if (!rvr && RaidName(eventId) is { } raid)
                shared["raid"] = raid;
            if (rvr)
            {
                // the real enemy: the realm the notice names, else the event's attacker/defender, else the keep's
                // owner when it is not ours; only then a random one (owner 2026-11-10)
                eRealm enemy = BanterLibrary165.RealmNamedIn(text, realm);
                if (enemy == eRealm.None)
                {
                    (eRealm attacker, eRealm defender) = AutonomousRvrEventLayer.EventRealms(eventId);
                    enemy = BanterLibrary165.EnemyOf(realm, attacker, defender);
                }
                if (enemy == eRealm.None && keep != null && keep.Realm != realm)
                    enemy = keep.Realm;
                if (enemy != eRealm.None)
                    shared["enemyrealm"] = BanterLibrary165.RealmName(enemy);
            }
            GameBot[] speakers = ChooseSpeakers(realm, eventId, heraldName, Random.Shared.NextDouble() < 0.4 ? 2 : 1);
            SayOnRealm(speakers, realm, situation, shared, 2_500);
        }

        /// <summary>A player sounded the Horn of Rallying: one or two bots of the realm answer the call.</summary>
        public static void OnHornSounded(GamePlayer player, string raidName)
        {
            if (player == null || Current.CountFor("rally_horn") == 0)
                return;
            long now = GameLoop.GameLoopTime;
            if (LastHorn.TryGetValue(player.Realm, out long last) && now - last < 60_000)
                return;
            LastHorn[player.Realm] = now;
            var shared = new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = player.Name };
            if (!string.IsNullOrWhiteSpace(raidName))
                shared["raid"] = raidName;
            GameBot[] speakers = ChooseSpeakers(player.Realm, null, player.Name, Random.Shared.NextDouble() < 0.5 ? 2 : 1);
            SayOnRealm(speakers, player.Realm, "rally_horn", shared, 3_000);
        }

        // Reservoir pick over the registry: event participants first, then anyone of the realm. Runs once per notice.
        private static GameBot[] ChooseSpeakers(eRealm realm, string eventId, string exclude, int count)
        {
            Dictionary<string, string> forces = eventId != null && eventId.StartsWith("rvr-", StringComparison.Ordinal)
                ? AutonomousRvrEventLayer.ForceTargets() : null;
            var inEvent = new List<GameBot>();
            var others = new List<GameBot>();
            int seenInEvent = 0, seenOthers = 0;
            foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            {
                if (bot.Realm != realm || !CanTalk(bot) || string.Equals(bot.Name, exclude, StringComparison.OrdinalIgnoreCase))
                    continue;
                bool participant = eventId != null &&
                    (forces != null
                        ? forces.TryGetValue(bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}", out string target) && target == eventId
                        : bot.Group != null && AutonomousRealmRaid.GetView(bot.Group)?.EventId == eventId);
                if (participant) Reservoir(inEvent, bot, ++seenInEvent, count);
                else Reservoir(others, bot, ++seenOthers, count);
            }
            return inEvent.Concat(others).Take(count).ToArray();
        }

        private static void Reservoir(List<GameBot> picked, GameBot bot, int seen, int count)
        {
            if (picked.Count < count)
                picked.Add(bot);
            else
            {
                int slot = Random.Shared.Next(seen);
                if (slot < count)
                    picked[slot] = bot;
            }
        }

        private static void SayOnRealm(GameBot[] speakers, eRealm realm, string situation, Dictionary<string, string> shared, int firstDelay)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < speakers.Length; index++)
            {
                GameBot speaker = speakers[index];
                Dictionary<string, string> values = Read(speaker).Values;
                foreach ((string key, string value) in shared)
                    values[key] = value;
                string line = Pick([situation], "realm", speaker, values, null, out _);
                if (line == null || !used.Add(line))
                    continue;
                _ = new ECSGameTimer(speaker, _ =>
                {
                    if (CanTalk(speaker) && ClientService.Instance.GetPlayersOfRealm(realm).Count > 0)
                        AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, line);
                    return 0;
                }, firstDelay + index * 2_200 + Random.Shared.Next(1_500));
            }
        }

        /// <summary>
        /// A player spoke in party chat (/g): a gamebot in the party may answer there (always the one named; otherwise
        /// 40% of the time), at most once per 10 seconds per player. Hired companions keep their own /say talk.
        /// </summary>
        public static void OnPlayerGroupChat(GamePlayer player, string message)
        {
            Group group = player?.Group;
            if (group == null || string.IsNullOrWhiteSpace(message))
                return;
            GameBot[] bots = group.GetMembersInTheGroup().OfType<GameBot>().Where(CanTalk).ToArray();
            if (bots.Length == 0)
                return;
            GameBot named = bots.FirstOrDefault(bot => AutonomousBotChatCoordinator.ContainsMention(message, bot.Name));
            if (named == null && Random.Shared.NextDouble() > 0.4)
                return;
            long now = GameLoop.GameLoopTime;
            string key = player.InternalID ?? player.Name;
            if (LastGroupReply.TryGetValue(key, out long last) && now - last < 10_000)
                return;
            LastGroupReply[key] = now;
            GameBot speaker = named ?? bots[Random.Shared.Next(bots.Length)];
            // no built-in scolding for a party "ty" / "rez pls" the library had no line for (owner 2026-11-10)
            bool rescued = AutonomousChatIntentModel.Predict(message) == eAutonomousChatIntent.IgnoreOutOfWorld &&
                           BanterLibrary165.PeriodWordRescues(message);
            string reply = TryPlayerReply(speaker, player, message, "group", named != null) ??
                           (rescued ? null : AutonomousBotChatCoordinator.GenerateLocalReply(speaker, message));
            if (string.IsNullOrWhiteSpace(reply))
                return;
            _ = new ECSGameTimer(speaker, _ =>
            {
                if (CanTalk(speaker) && speaker.Group == group)
                    group.SendMessageToGroupMembers(speaker, reply, eChatType.CT_Group, eChatLoc.CL_ChatWindow);
                return 0;
            }, 1_400 + Random.Shared.Next(1_200));
        }

        #endregion
    }
}

namespace DOL.GS.Commands
{
    /// <summary>GM: /banter reload | stats - re-read banter165/*.json without a restart (owner 2026-11-10).</summary>
    [CmdAttribute("&banter", ePrivLevel.GM, "Reload or count the bot banter lines (banter165 folder)", "/banter reload|stats")]
    public sealed class BanterCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null)
                return;
            string verb = args.Length > 1 ? args[1].ToLowerInvariant() : "stats";
            if (verb == "reload")
            {
                DisplayMessage(client, BotBanter165.Reload());
                return;
            }
            BanterLibrary165.BanterSet set = BotBanter165.Current;
            DisplayMessage(client, $"Banter: {set.Count} lines from {set.Files} files, {set.Errors.Count} problems ({BotBanter165.FolderPath ?? "no folder"}).");
            foreach (string situation in BanterLibrary165.Situations.Keys.Where(key => set.CountFor(key) > 0))
                DisplayMessage(client, $"  {situation}: {set.CountFor(situation)}");
        }
    }
}
