using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DOL.Database;
using DOL.Database.Connection;
using DOL.Events;

namespace DOL.GS
{
    /// <summary>
    /// Unique names for bots, hired companions and players (True online world, plan 3.5). Backed by the claim table
    /// offline_bot_names (BotNameRegistry) and the race-styled generator (BotNameGenerator, tables in
    /// config\botnames165.json, reloaded live). Opens and seeds itself in the background at server start; every call is
    /// safe before that (bot names wait, player checks fall back to the old tables). Nothing here throws.
    /// Database work runs on the caller's thread with a short try-lock: call the claim methods off the game loop.
    /// </summary>
    public static class BotNameService
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        private static volatile BotNameRegistry _registry;
        private static volatile BotNameGenerator _generator;
        private static readonly object InitLock = new();
        private static string _tablesPath, _invalidNamesPath;
        private static (DateTime, long) _tablesStamp, _invalidStamp;

        /// <summary>True once the claim table is open and seeded.</summary>
        public static bool IsReady => _registry?.Ready == true;

        /// <summary>The generator in use (built-in tables until the server has loaded its file).</summary>
        public static BotNameGenerator Generator
        {
            get
            {
                BotNameGenerator g = _registry?.Generator ?? _generator;
                if (g != null)
                    return g;
                lock (InitLock)
                {
                    try
                    {
                        _generator ??= BotNameGenerator.BuiltIn();
                    }
                    catch (Exception ex)
                    {
                        log.Error("BOT_NAMES built-in tables could not be read", ex);
                    }
                    return _generator;
                }
            }
        }

        // ---- the spec API

        /// <summary>Claims a fresh, race-styled name for a new bot. False when the table is not ready or busy (try again
        /// later). Live, never throws.</summary>
        public static bool TryClaimNewBotName(eRealm realm, eRace race, eGender gender, long botId, out string name)
        {
            name = null;
            try
            {
                return _registry?.TryClaimNewBotName(realm, race, gender, botId, out name) == true;
            }
            catch (Exception ex)
            {
                log.Error("BOT_NAMES claim failed", ex);
                name = null;
                return false;
            }
        }

        /// <summary>Character creation (atomic): false when a bot or player already wears the name.</summary>
        public static bool TryClaimPlayerName(string name)
        {
            try
            {
                BotNameRegistry registry = _registry;
                return registry != null ? registry.TryClaimPlayerName(name) : !LegacyTaken(name);
            }
            catch (Exception ex)
            {
                log.Error("BOT_NAMES player claim failed", ex);
                return true;
            }
        }

        /// <summary>Duplicate-name check and hire window: a bot (warm or archived) or a player wears it.</summary>
        public static bool IsNameTaken(string name)
        {
            try
            {
                BotNameRegistry registry = _registry;
                return registry != null ? registry.IsNameTaken(name) : LegacyTaken(name);
            }
            catch (Exception ex)
            {
                log.Error("BOT_NAMES name check failed", ex);
                return false;
            }
        }

        /// <summary>Retirement: the name cools down for 30 days before a bot may get it again.</summary>
        public static void ReleaseBotName(string name) => Safe(r => r.ReleaseBotName(name));

        /// <summary>Character deletion: the name cools down (a player may take it again at once).</summary>
        public static void ReleasePlayerName(string name) => Safe(r => r.ReleasePlayerName(name));

        public static void MarkArchived(string name, bool archived) => Safe(r => r.MarkArchived(name, archived));

        // ---- extras

        /// <summary>Records the bot id on a name claimed before the bot's row existed (queued).</summary>
        public static void SetBotId(string name, long botId) => Safe(r => r.SetBotId(name, botId));

        /// <summary>A hand-picked bot name (hired companion), same rules as a player's. True when claimed (or when the
        /// table is not open, after the old-table check).</summary>
        public static bool TryClaimBotName(string name, long botId)
        {
            try
            {
                BotNameRegistry registry = _registry;
                return registry != null ? registry.TryClaimBotName(name, botId) : !LegacyTaken(name);
            }
            catch (Exception ex)
            {
                log.Error("BOT_NAMES companion claim failed", ex);
                return true;
            }
        }

        /// <summary>A race-styled name nobody wears, not claimed (hire window suggestions, /tg helpers).</summary>
        public static string SuggestName(eRealm realm, eRace race, eGender gender, Random random = null)
        {
            try
            {
                if (BotNameGenerator.RealmOf(race) == eRealm.None)
                {
                    var races = BotNameGenerator.StyleRaces.Where(r => BotNameGenerator.RealmOf(r) == realm).ToArray();
                    if (races.Length == 0)
                        return null;
                    race = races[(random ?? Random.Shared).Next(races.Length)];
                }
                return Generator?.RandomName(race, gender, random ?? Random.Shared, IsNameTaken, attempts: 40);
            }
            catch (Exception ex)
            {
                log.Error("BOT_NAMES suggestion failed", ex);
                return null;
            }
        }

        private static void Safe(Action<BotNameRegistry> action)
        {
            try
            {
                if (_registry is { } registry)
                    action(registry);
            }
            catch (Exception ex)
            {
                log.Error("BOT_NAMES queued write failed", ex);
            }
        }

        // ---- tests

        /// <summary>Plugs in a registry (tests); null detaches it.</summary>
        public static void UseRegistryForTests(BotNameRegistry registry) => _registry = registry;

        // ---- server start / stop

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            Task.Run(Start);
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            BotNameRegistry registry = _registry;
            _registry = null;
            try
            {
                registry?.Dispose();
            }
            catch (Exception ex)
            {
                log.Error("BOT_NAMES shutdown", ex);
            }
        }

        private static void Start()
        {
            try
            {
                GameServerConfiguration config = GameServer.Instance?.Configuration;
                if (config == null)
                    return;
                _invalidNamesPath = config.InvalidNamesFile;
                string configDir = Path.GetDirectoryName(Path.GetFullPath(_invalidNamesPath ?? Path.Combine(".", "config", "invalidnames.txt")));
                _tablesPath = Path.Combine(configDir ?? ".", BotNameGenerator.FileName);
                BotNameGenerator generator = LoadGenerator(out string source);
                lock (InitLock)
                    _generator = generator;
                if (config.DBType != EConnectionType.DATABASE_SQLITE)
                {
                    log.Warn("BOT_NAMES the name table needs SQLite; names are checked against the old tables only.");
                    return;
                }
                object gate = FindServerWriteGate();
                var registry = new BotNameRegistry(new BotNameRegistry.Options
                {
                    ConnectionString = config.DBConnectionString,
                    WriteGate = gate,
                    LiveBlocked = name => GameServer.Instance?.PlayerManager?.InvalidNames[name] ?? false,
                    LegacyTaken = LegacyTaken,
                    ProtectedNames = FriendAndIgnoreNames,
                }, generator);
                registry.Open();
                registry.Housekeeping = Housekeeping;
                _registry = registry;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                registry.SeedFromWorld();
                log.Info($"BOT_NAMES ready: tables from {source}, {registry.Count(BotNameRegistry.NameState.Warm)} bot, " +
                         $"{registry.Count(BotNameRegistry.NameState.Archived)} archived, {registry.Count(BotNameRegistry.NameState.Player)} player, " +
                         $"{registry.Count(BotNameRegistry.NameState.Reserved)} NPC and {registry.Count(BotNameRegistry.NameState.Cooling)} cooling names " +
                         $"(seeded in {watch.ElapsedMilliseconds} ms, write gate {(gate != null ? "shared" : "own")}).");
            }
            catch (Exception ex)
            {
                log.Error("BOT_NAMES could not start; names are checked against the old tables only", ex);
            }
        }

        /// <summary>The tables file if valid, else the built-in tables.</summary>
        private static BotNameGenerator LoadGenerator(out string source)
        {
            BotNameBlocklist invalid = BotNameBlocklist.FromFile(_invalidNamesPath, null);
            _invalidStamp = Stamp(_invalidNamesPath);
            _tablesStamp = Stamp(_tablesPath);
            if (_tablesPath != null && File.Exists(_tablesPath))
            {
                try
                {
                    var tables = BotNameTables.Parse(File.ReadAllText(_tablesPath));
                    string problem = BotNameGenerator.Validate(tables);
                    if (problem == null)
                    {
                        source = _tablesPath;
                        return new BotNameGenerator(tables, invalid);
                    }
                    log.Warn($"BOT_NAMES {_tablesPath}: {problem} Using the built-in tables.");
                }
                catch (Exception ex)
                {
                    log.Warn($"BOT_NAMES {_tablesPath} could not be read ({ex.Message}); using the built-in tables.");
                }
            }
            source = "the built-in tables";
            return BotNameGenerator.BuiltIn(invalid);
        }

        /// <summary>Every ~30 s on the name thread: reloads the tables or invalidnames.txt when either file changed.</summary>
        private static void Housekeeping()
        {
            if (Stamp(_tablesPath) == _tablesStamp && Stamp(_invalidNamesPath) == _invalidStamp)
                return;
            try
            {
                BotNameGenerator generator = LoadGenerator(out string source);
                lock (InitLock)
                    _generator = generator;
                _registry?.ReplaceGenerator(generator);
                log.Info($"BOT_NAMES tables reloaded from {source}.");
            }
            catch (Exception ex)
            {
                log.Error("BOT_NAMES reload failed; keeping the old tables", ex);
            }
        }

        private static (DateTime, long) Stamp(string path)
        {
            try
            {
                var info = new FileInfo(path ?? string.Empty);
                return info.Exists ? (info.LastWriteTimeUtc, info.Length) : default;
            }
            catch (Exception)
            {
                return default;
            }
        }

        /// <summary>The server's SQLite write gate (protected SqlObjectDatabase.WriteSerializationLock), so name writes
        /// queue with the server's own writes instead of hitting SQLITE_BUSY. Null (own lock) when not found.</summary>
        private static object FindServerWriteGate()
        {
            try
            {
                object database = GameServer.Database;
                for (Type type = database?.GetType(); type != null; type = type.BaseType)
                {
                    PropertyInfo property = type.GetProperty("WriteSerializationLock", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                    if (property != null)
                        return property.GetValue(database);
                }
            }
            catch (Exception ex)
            {
                log.Warn($"BOT_NAMES write gate not found ({ex.Message}); using a private lock.");
            }
            return null;
        }

        /// <summary>Old checks: a bot record or a character with the name (used until the table is seeded).</summary>
        private static bool LegacyTaken(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || GameServer.Instance == null)
                return false;
            name = name.Trim();
            return DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("Name").IsEqualTo(name)) != null ||
                   DOLDB<DbCoreCharacter>.SelectObject(DB.Column("Name").IsEqualTo(name)) != null;
        }

        /// <summary>Every name on a player's friend or ignore list (saved lists plus online players' current ones).</summary>
        private static IEnumerable<string> FriendAndIgnoreNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddList(string list)
            {
                if (string.IsNullOrEmpty(list))
                    return;
                foreach (string n in list.Split(','))
                    if (n.Trim().Length > 0)
                        names.Add(n.Trim());
            }
            _registry?.Query(c =>
            {
                if (!BotNameRegistry.HasTable(c, null, "DOLCharacters"))
                    return 0;
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT SerializedFriendsList, SerializedIgnoreList FROM DOLCharacters";
                using SQLiteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    AddList(reader.IsDBNull(0) ? null : reader.GetString(0));
                    AddList(reader.IsDBNull(1) ? null : reader.GetString(1));
                }
                return 0;
            });
            try
            {
                foreach (GamePlayer player in ClientService.Instance.GetPlayers())
                {
                    foreach (string n in player.SerializedFriendsList)
                        names.Add(n);
                    foreach (string n in player.SerializedIgnoreList)
                        names.Add(n);
                }
            }
            catch (Exception) { }
            return names;
        }
    }
}
