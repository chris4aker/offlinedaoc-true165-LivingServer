using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using DOL.AI.Brain;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Opt-in, stationary human-party pull scheduler. It never owns a bot goal,
    /// changes a combat brain, or runs for a persistent playerbot group.
    /// </summary>
    public static class PlayerCompanionGrind
    {
        public const int PullRadius = 1250;
        public const int MaxPullRadius = 2500;

        /// <summary>
        /// How far /grind looks for its next monster (owner 2026-11-09: "the target selection range for /grind is too
        /// short" - a crossbow reaches 1,200 and pull spells 1,500, but only monsters within 1,000 were picked): the
        /// puller's own reach less a step, plus a quarter (owner 2026-11-11: /grind range +25%), never under 1,250 (a
        /// melee puller walks up as before), at most 2,500.
        /// </summary>
        public static int SearchRadius(int pullerReach) => Math.Clamp((pullerReach - 100) * 5 / 4, PullRadius, MaxPullRadius);
        public const int RegroupRadius = 350;

        /// <summary>
        /// How far from the player a /grind pull order may reach (PlayerLedPullCoordinator.Begin): the search radius plus
        /// the regroup radius (a companion at camp is up to 350 units from the player), never under the 2,000 a manual
        /// /pull uses.
        /// </summary>
        public static int PullOrderRange(int pullerReach) =>
            Math.Max(BotBrain.GROUP_DEFENSE_ASSIST_RADIUS, SearchRadius(pullerReach) + RegroupRadius);
        public const int ContactTimeout = 35_000;
        // The legacy native client can close an otherwise healthy localhost
        // session after a long period with no player-originated movement. A
        // same-coordinate CharacterJump is the protocol's normal position
        // resynchronization packet; it creates no movement or combat action.
        private const int ClientSessionRefreshInterval = 4 * 60_000;
        private static readonly ConcurrentDictionary<GamePlayer, Session> Sessions = new();
        private static readonly Logger log = LoggerManager.Create(typeof(PlayerCompanionGrind));
        private static System.Threading.Timer _timer;

        private sealed class Session
        {
            public GamePlayer Player;
            public Group Group;
            public ushort Region;
            public Vector3 Anchor;
            public int Queued;
            public GameNPC Target;
            public long ContactDeadline;
            public long ReadySince;
            public long NextSearch;
            public long NextStatus;
            public long NextClientSessionRefresh;
            public string Detail = "Waiting for the party";
            public string PullerName;                   // "/grind (name)": that companion pulls (null: chosen each pull)
            public readonly HashSet<GameLiving> SeatedByMode = new();
            public readonly Dictionary<GameNPC, long> FailedTargets = new();
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ =>
            {
                foreach (Session session in Sessions.Values)
                    if (Interlocked.Exchange(ref session.Queued, 1) == 0)
                        NpcService.Instance.Post(Tick, session);
            }, null, 1000, 1000);
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Dispose(); _timer = null; Sessions.Clear();
        }

        public static bool IsActive(GamePlayer player) => player != null && Sessions.ContainsKey(player);

        // Your own companions: /spawn helpers, and in True 1.65 Mode your hired companions (owner 2026-11-08: /grind back
        // for the hired group).
        public static bool IsOwnedCompanion(GamePlayer player, Group group, GameLiving member) =>
            player != null && group != null && member is GameBot bot && bot.Group == group && bot.Realm == player.Realm &&
            (bot.IsTemporaryGroupHelper && !bot.IsAutonomousWorldBot && bot.Owner == player ||
             HiredCompanions165.IsHired(bot) && bot.CompanionPlayer == player);

        // True 1.65 Mode: /grind is a quality of life option, off by default (the client's Settings > Interface > Grinding > GRIND sends
        // "/grind option on|off quiet" at every world entry and on a change; owner 2026-11-08).
        private static readonly ConcurrentDictionary<GamePlayer, bool> Enabled = new();
        public static bool IsEnabled(GamePlayer player) => !Classic165Mode.IsOn || player != null && Enabled.ContainsKey(player);
        public static void SetEnabled(GamePlayer player, bool on)
        {
            if (player == null) return;
            if (on) Enabled[player] = true;
            else
            {
                Enabled.TryRemove(player, out _);
                if (IsActive(player)) Stop(player, "turned off in Options");
            }
        }

        public static bool IsExclusiveParty(GamePlayer player, Group group, IReadOnlyList<GameLiving> members) =>
            player != null && group != null && player.Group == group && members.Count is >= 2 and <= 8 &&
            members.Count(member => member == player) == 1 &&
            members.All(member => member == player || IsOwnedCompanion(player, group, member));

        public static bool NeedsRecovery(GameLiving member) => member.MaxHealth > 0 && member.Health < member.MaxHealth ||
            member.MaxMana > 0 && member.Mana < member.MaxMana ||
            member.MaxEndurance > 0 && member.Endurance < member.MaxEndurance;

        // /grind stays careful: everyone must be at full health and not casting before an
        // automatic pull. Classes that keep chants or songs running (Paladin, Warden, Bard,
        // Minstrel, Skald) never refill power/endurance completely and are casting their
        // songs much of the time, which blocked /grind forever; for them a song cast does
        // not count and power/endurance only need to reach this floor.
        public const int SongUpkeepResourceFloorPercent = 75;

        public static bool IsSongUpkeepClass(eCharacterClass characterClass) =>
            BotBrain.IsClassicSongClass(characterClass) || characterClass is eCharacterClass.Paladin or eCharacterClass.Warden;

        public static bool ReadyWithSongUpkeep(int health, int maxHealth, int mana, int maxMana, int endurance, int maxEndurance) =>
            (maxHealth <= 0 || health >= maxHealth) &&
            (maxMana <= 0 || mana * 100L >= maxMana * (long)SongUpkeepResourceFloorPercent) &&
            (maxEndurance <= 0 || endurance * 100L >= maxEndurance * (long)SongUpkeepResourceFloorPercent);

        public static bool BlocksAutomaticPull(GameLiving member)
        {
            if (member.IsCrowdControlled)
                return true;
            if (!KeepsSongsUp(member))
                return NeedsRecovery(member) || member.IsCasting;
            return !ReadyWithSongUpkeep(member.Health, member.MaxHealth, member.Mana, member.MaxMana, member.Endurance, member.MaxEndurance) ||
                member.IsCasting && member.castingComponent?.SpellHandler?.Spell?.IsPulsing != true && !BotSongTwistPolicy.HasMobileSongCast(member);
        }

        /// <summary>A chanter or singer (Paladin, Warden, Bard, Minstrel, Skald), a companion or the player.</summary>
        public static bool KeepsSongsUp(GameLiving member)
        {
            int classId = member switch
            {
                GameBot bot => bot.CharacterClass?.ID ?? 0,
                GamePlayer player => player.CharacterClass?.ID ?? 0,
                _ => 0,
            };
            return classId != 0 && IsSongUpkeepClass((eCharacterClass)classId);
        }

        /// <summary>
        /// Whether /grind sits this member down between pulls (owner 2026-11-09: "he should wait til he is below a certain
        /// threshold before needing to sit"). A chanter's or singer's own chant costs power every pulse, so "below full"
        /// sat a Paladin down, he refilled in a second, stood up, and his chant drained him below full again - up and down
        /// without end, and every power-draining chant or aura was unusable in /grind. Chanters and singers sit only below
        /// the pull floor (75% power or endurance) or when hurt; seated, they still rest to full (sitting easily outpaces a
        /// chant's drain). Everyone else sits whenever they are below full, as before.
        /// </summary>
        public static bool NeedsModeRest(GameLiving member) => KeepsSongsUp(member)
            ? !ReadyWithSongUpkeep(member.Health, member.MaxHealth, member.Mana, member.MaxMana, member.Endurance, member.MaxEndurance)
            : NeedsRecovery(member);

        // Who pulls (owner 2026-11-08): a tank that can pull from range first (the monster ends up on the tank), then a
        // ranged damage dealer (an archer with ammunition, a caster with a ranged spell), then a tank that walks up, makes
        // contact and walks back, then any other attacker (walks up too). Healers and buffers never pull. Every pull is brought back to the group (/pull's True
        // 1.65 bring-back).
        public static GameBot SelectPuller(IEnumerable<GameBot> companions) =>
            SelectPuller(companions, AutonomousDefensivePull.HasRangedPull);

        public static GameBot SelectPuller(IEnumerable<GameBot> companions, Func<GameBot, bool> rangedPull) => companions
            .Where(CanPull)
            .Select(bot => (Bot: bot, Rank: PullerRank(BotPartyRoles.IsTank(bot), rangedPull(bot))))
            .OrderBy(pick => pick.Rank).ThenBy(pick => pick.Bot.ObjectID).Select(pick => pick.Bot).FirstOrDefault();

        /// <summary>0 a tank with a ranged pull, 1 a ranged damage dealer, 2 a tank that walks up, 3 another attacker.</summary>
        public static int PullerRank(bool tank, bool rangedPull) => tank && rangedPull ? 0 : rangedPull ? 1 : tank ? 2 : 3;

        public static bool CanPull(GameBot bot) => bot != null && bot.IsAlive && !bot.IsCrowdControlled && bot.CharacterClass != null &&
            BotPartyRoles.For((eCharacterClass)bot.CharacterClass.ID) != BotPartyRole.Support;

        private static bool IsSupport(GameBot bot) => bot?.CharacterClass != null &&
            BotPartyRoles.For((eCharacterClass)bot.CharacterClass.ID) == BotPartyRole.Support;

        /// <summary>The companion named in "/grind (name)" (whole name first, else the start of a name).</summary>
        public static GameBot FindNamed(IEnumerable<GameBot> companions, string name) =>
            string.IsNullOrWhiteSpace(name) ? null : PlayerLedPullCoordinator.FindNamed(companions.ToArray(), name.Trim());

        public static bool IsSuitableLevel(int playerLevel, int monsterLevel, int conLevel, int companionCount) =>
            monsterLevel > 0 && conLevel > -3;

        /// <summary>"/grind (name)" while grinding: that companion pulls from now on.</summary>
        public static string SetPuller(GamePlayer player, string name)
        {
            if (!Sessions.TryGetValue(player, out Session session))
                return "Grind mode is not active.";
            GameBot bot = FindNamed(session.Group.GetMembersInTheGroup().OfType<GameBot>()
                .Where(member => IsOwnedCompanion(player, session.Group, member)), name);
            if (bot == null) return $"No companion of yours named {name.Trim()} is in your group.";
            if (IsSupport(bot)) return $"{bot.Name} heals and supports the group and does not pull.";
            session.PullerName = bot.Name;
            return $"{bot.Name} pulls from now on. /grind stop, then /grind alone, goes back to choosing the puller each pull.";
        }

        public static bool TryStart(GamePlayer player, out string message) => TryStart(player, null, out message);

        public static bool TryStart(GamePlayer player, string pullerName, out string message)
        {
            if (!IsEnabled(player))
            { message = "/grind is off. Turn it on in Settings > Interface > Grinding > GRIND (a quality of life option, not in 1.65)."; return false; }
            if (player?.Client?.ClientState != GameClient.eClientState.Playing || !player.IsAlive)
            { message = "You must be alive and in the world to use /grind."; return false; }
            if (IsActive(player))
            { message = "Grind mode is already active. Use /grind stop to end it."; return false; }
            if (AutonomousPlayerPilot.IsActive(player) || PlayerMobNavigator.IsActive(player))
            { message = "Stop any active automated movement before starting stationary /grind mode."; return false; }
            if (player.Steed != null || player.IsOnHorse)
            { message = "Finish your ride before using /grind."; return false; }
            GameLiving[] members = player.Group?.GetMembersInTheGroup().ToArray() ?? Array.Empty<GameLiving>();
            if (!IsExclusiveParty(player, player.Group, members))
            { message = "/grind needs a group of only you and your own companions."; return false; }
            string named = null;
            if (!string.IsNullOrWhiteSpace(pullerName))
            {
                GameBot bot = FindNamed(members.OfType<GameBot>(), pullerName);
                if (bot == null) { message = $"No companion of yours named {pullerName.Trim()} is in your group."; return false; }
                if (IsSupport(bot)) { message = $"{bot.Name} heals and supports the group and does not pull."; return false; }
                named = bot.Name;
            }
            if (named == null && SelectPuller(members.OfType<GameBot>()) == null)
            { message = "You need a living tank or damage-dealing companion. Healers and buffers will not pull."; return false; }
            var session = new Session { Player = player, Group = player.Group, Region = player.CurrentRegionID, PullerName = named,
                Anchor = new(player.X, player.Y, player.Z),
                NextClientSessionRefresh = GameLoop.GameLoopTime + ClientSessionRefreshInterval };
            if (!Sessions.TryAdd(player, session))
            { message = "Grind mode is already active."; return false; }
            // Park once, not a movement-suppression loop. Walking manually cancels
            // the mode instead of fighting the client's movement or teleporting it.
            player.CurrentSpeed = 0;
            player.Out.SendPlayerJump(false);
            ShowStatus(session, true);
            message = (named != null ? $"Grind mode active: {named} pulls and brings each monster back to you. " :
                "Grind mode active: a tank that can pull from range pulls first, then an archer or caster, then the tank walks " +
                "up; every pull is brought back to you. ") +
                "The next pull waits for everyone to return and fully recover HP/power/endurance. /grind stop cancels; moving also stops it.";
            return true;
        }

        public static void Stop(GamePlayer player, string reason, bool notify = true)
        {
            if (player == null || !Sessions.TryRemove(player, out Session session)) return;
            CancelUnengagedPull(session);
            StandModeSeats(session);
            if (notify && player.Client?.ClientState == GameClient.eClientState.Playing)
            {
                player.Out.SendMessage($"GRIND MODE OFF — {reason}", eChatType.CT_ScreenCenter, eChatLoc.CL_SystemWindow);
                player.Out.SendMessage($"/grind stopped: {reason}", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }
        }

        private static bool Fighting(GameLiving living) => living.InCombat ||
            living.ControlledBrain?.Body is { IsAlive: true, InCombat: true };

        private static bool CombatIntent(GameLiving living) => Fighting(living) || living.IsAttacking ||
            living is GameBot { Brain: BotBrain { HasAggro: true } } ||
            living.ControlledBrain?.Body is { IsAlive: true, IsAttacking: true };

        private static bool AtCamp(Session session, GameLiving member) => member.IsAlive &&
            member.ObjectState == GameObject.eObjectState.Active && member.CurrentRegionID == session.Region &&
            member.IsWithinRadius(session.Player, RegroupRadius) &&
            member is not GameBot { IsReturningAfterRelease: true } && member is not GameBot { IsOnStableMasterRoute: true };

        private static void Tick(Session session)
        {
            try
            {
                GamePlayer player = session.Player;
                if (!Sessions.TryGetValue(player, out var current) || current != session) return;
                if (player.Client?.ClientState != GameClient.eClientState.Playing || player.ObjectState != GameObject.eObjectState.Active)
                { Stop(player, "left the world", false); return; }
                if (!player.IsAlive) { Stop(player, "you died"); return; }
                if (player.CurrentRegionID != session.Region || player.IsMoving ||
                    Vector3.DistanceSquared(session.Anchor, new(player.X, player.Y, player.Z)) > 64 * 64 ||
                    player.Steed != null || player.IsOnHorse ||
                    AutonomousPlayerPilot.IsActive(player) || PlayerMobNavigator.IsActive(player))
                { Stop(player, "you moved, traveled or enabled another controller"); return; }
                GameLiving[] members = player.Group?.GetMembersInTheGroup().ToArray() ?? Array.Empty<GameLiving>();
                if (player.Group != session.Group || !IsExclusiveParty(player, session.Group, members))
                { Stop(player, "party changed; only your temporary companions are allowed"); return; }
                long now = GameLoop.GameLoopTime;
                if (now >= session.NextClientSessionRefresh)
                {
                    session.NextClientSessionRefresh = now + ClientSessionRefreshInterval;
                    // Also keep the server-side activity timestamp current if a
                    // portable install later enables the ordinary idle policy.
                    player.LastPlayerActivityTime = now;
                    player.Out.SendPlayerJump(false);
                }
                bool actualCombat = members.Any(Fighting);
                if (session.Target != null)
                {
                    if (!session.Target.IsAlive || session.Target.ObjectState != GameObject.eObjectState.Active)
                        session.Target = null;
                    else if (actualCombat)
                        session.ContactDeadline = now + ContactTimeout;
                    else if (now >= session.ContactDeadline)
                    {
                        GameNPC failed = session.Target;
                        CancelUnengagedPull(session);
                        session.FailedTargets[failed] = now + 120_000;
                        session.Target = null; session.NextSearch = now + 3000;
                        session.Detail = $"Skipping unreachable pull: {failed.Name}";
                    }
                }
                if (actualCombat || members.Any(CombatIntent) || session.Target != null)
                {
                    session.ReadySince = 0;
                    if (actualCombat) StandModeSeats(session);
                    string targetName = session.Target == null ? "nearby attackers" : $"{session.Target.Name} ({session.Target.Level})";
                    session.Detail = actualCombat ? $"Party fighting {targetName} — no new pulls" : $"Pulling {targetName}; waiting for contact";
                    return;
                }

                // Native companion heal/buff AI remains in charge. Do not run its
                // think loop here, stop beneficial casts, or refill resources.
                foreach (GameLiving member in members)
                {
                    if (AtCamp(session, member) && !member.IsCasting && !member.IsMoving &&
                        !member.IsCrowdControlled && NeedsModeRest(member) &&
                        (member is not GameBot helper || !helper.IsTemporaryCompanionRestLocked) &&
                        (member is not GameBot restingBot || !BotRestRecovery.BlocksRest(restingBot)) &&
                        (member is GameBot || !member.IsSitting))
                    {
                        session.SeatedByMode.Add(member);
                        if (member == player) player.Sit(true);
                        else if (((GameBot)member).IsTemporaryGroupHelper) ((GameBot)member).BeginTemporaryCompanionRest();
                        else ((GameBot)member).BeginRecoveryRest();   // a hired companion (woken by WakeTemporaryCompanionRest)
                    }
                }
                if (members.Any(member => !AtCamp(session, member)))
                { session.ReadySince = 0; session.Detail = "Waiting for companions to revive/return to camp"; return; }
                if (members.Any(BlocksAutomaticPull))
                { session.ReadySince = 0; session.Detail = "Resting/healing/buffing — waiting for full HP, power and endurance (75% for chanters and singers)"; return; }
                GameBot puller = session.PullerName != null
                    ? FindNamed(members.OfType<GameBot>(), session.PullerName) is { } chosen && CanPull(chosen) ? chosen : null
                    : SelectPuller(members.OfType<GameBot>());
                if (puller == null)
                {
                    session.ReadySince = 0;
                    session.Detail = session.PullerName != null ? $"Waiting for {session.PullerName} to be able to pull"
                        : "Waiting for a tank or attacker (support classes will not pull)";
                    return;
                }
                if (session.ReadySince == 0) session.ReadySince = now;
                if (now - session.ReadySince < 2000 || now < session.NextSearch) return;
                session.NextSearch = now + 5000;
                GameNPC target = FindTarget(session, puller, members.Length - 1, now);
                if (target == null)
                { session.Detail = "Ready — no suitable reachable nearby monsters"; return; }
                StandModeSeats(session);
                foreach (GameBot helper in members.OfType<GameBot>()) helper.WakeTemporaryCompanionRest();
                session.Target = target; session.ContactDeadline = now + ContactTimeout; session.ReadySince = 0;
                // This is precisely the existing /pull command's shared combat
                // dispatch, protected by the strict temporary-only party gate.
                // The order reaches as far as the search did (a manual /pull keeps 2,000).
                int reach = AutonomousDefensivePull.RangedPullReach(puller);
                int orderRange = PullOrderRange(reach);
                if (log.IsInfoEnabled) log.Info($"GRIND_PULL_RANGE player=\"{player.Name}\" puller=\"{puller.Name}\" target=\"{target.Name}\" " +
                         $"distance={player.GetDistanceTo(target)} reach={reach} search={SearchRadius(reach)} range={orderRange}");
                session.Detail = PlayerLedPullCoordinator.Begin(player, target, puller.Name, orderRange);
            }
            catch (Exception exception)
            {
                Stop(session.Player, $"safety stop ({exception.GetType().Name}); manual control retained");
            }
            finally
            {
                if (Sessions.TryGetValue(session.Player, out var current) && current == session) ShowStatus(session);
                Volatile.Write(ref session.Queued, 0);
            }
        }

        private static GameNPC FindTarget(Session session, GameBot puller, int count, long now)
        {
            foreach (GameNPC expired in session.FailedTargets.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                session.FailedTargets.Remove(expired);
            while (session.FailedTargets.Count > 64) session.FailedTargets.Remove(session.FailedTargets.MinBy(pair => pair.Value).Key);
            GamePlayer player = session.Player;
            var nav = PathfindingProvider.Instance;
            if (puller.CurrentZone == null || !nav.HasNavmesh(puller.CurrentZone)) return null;
            // Local search only, at most four corridor checks per five seconds,
            // and only for explicitly active human grind sessions.
            foreach (GameNPC npc in player.GetNPCsInRadius((ushort)SearchRadius(AutonomousDefensivePull.RangedPullReach(puller))).Where(npc =>
                PlayerMobNavigator.IsExperienceMonster(npc) && npc.IsAlive && npc.ObjectState == GameObject.eObjectState.Active &&
                !npc.InCombat && !npc.IsAttacking && npc.CurrentZone == puller.CurrentZone &&
                !session.FailedTargets.ContainsKey(npc) &&
                IsSuitableLevel(player.Level, npc.Level, player.GetConLevel(npc), count) &&
                GameServer.ServerRules.IsAllowedToAttack(player, npc, true))
                .OrderBy(npc => Math.Abs(npc.Level - player.Level)).ThenBy(npc => player.GetDistanceTo(npc)).Take(4))
            {
                Vector3 start = new(puller.X, puller.Y, puller.Z), end = new(npc.X, npc.Y, npc.Z);
                // Conservative camp pulls: no chasing behind walls or through
                // whole dungeon wings. Existing combat routing handles the pull.
                if (!nav.HasLineOfSight(puller.CurrentZone, start, end, nav.BlockingDoorAvoidanceFilters) ||
                    !PlayerTravelPath.TryBuildCorridor(nav, puller.CurrentZone, start, end, out _))
                { session.FailedTargets[npc] = now + 120_000; continue; }
                return npc;
            }
            return null;
        }

        private static void StandModeSeats(Session session)
        {
            foreach (GameLiving member in session.SeatedByMode)
            {
                if (!member.IsAlive || member.ObjectState != GameObject.eObjectState.Active) continue;
                if (member == session.Player)
                {
                    if (member.IsSitting) session.Player.Sit(false);
                }
                else if (member is GameBot helper && helper.IsTemporaryCompanionRestLocked &&
                         IsOwnedCompanion(session.Player, session.Group, member))
                    helper.WakeTemporaryCompanionRest();
            }
            session.SeatedByMode.Clear();
        }

        private static void CancelUnengagedPull(Session session)
        {
            if (session.Target == null || session.Target.InCombat || session.Group.GetMembersInTheGroup().Any(Fighting)) return;
            // Finish any actual battle normally. Only an unengaged /grind order
            // is revoked; do not clear defensive aggro or other players' orders.
            foreach (GameLiving member in session.Group.GetMembersInTheGroup())
                if (IsOwnedCompanion(session.Player, session.Group, member) && !Fighting(member) && member is GameBot { Brain: BotBrain brain })
                    brain.CancelOrderedPull(session.Target);
            if (IsExclusiveParty(session.Player, session.Group, session.Group.GetMembersInTheGroup().ToArray()))
                PlayerLedPullCoordinator.OnGroupThreat(session.Player);
        }

        public static string StatusText(GamePlayer player)
        {
            if (!IsEnabled(player)) return "/grind is off (Settings > Interface > Grinding > GRIND).";
            if (!Sessions.TryGetValue(player, out Session session))
                return "Grind mode is not active. /grind starts it; /grind (name) makes that companion the puller.";
            return $"Grind mode: {session.Detail}. Puller: {session.PullerName ?? "chosen each pull"}.";
        }

        private static void ShowStatus(Session session, bool force = false)
        {
            if (!force && GameLoop.GameLoopTime < session.NextStatus) return;
            session.NextStatus = GameLoop.GameLoopTime + 5000;
            session.Player.Out.SendMessage($"GRIND MODE ACTIVE • {session.Detail}", eChatType.CT_ScreenCenter, eChatLoc.CL_SystemWindow);
        }
    }

    [CmdAttribute("&grind", ePrivLevel.Player, "Stationary automatic pulls with your companions", "/grind", "/grind <companion>", "/grind stop", "/grind status")]
    public sealed class PlayerGrindCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            if (args.Length >= 3 && args[1].Equals("option", StringComparison.OrdinalIgnoreCase))
            {
                // the client's Options switch (True 1.65 Mode: off by default)
                bool on = args[2].Equals("on", StringComparison.OrdinalIgnoreCase);
                PlayerCompanionGrind.SetEnabled(client.Player, on);
                if (args.Length < 4 || !args[3].Equals("quiet", StringComparison.OrdinalIgnoreCase))
                    DisplayMessage(client, on ? "/grind is on." : "/grind is off.");
                return;
            }
            if (args.Length == 2 && args[1].Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                if (PlayerCompanionGrind.IsActive(client.Player)) PlayerCompanionGrind.Stop(client.Player, "cancelled by you");
                else DisplayMessage(client, "Grind mode is not active.");
                return;
            }
            if (args.Length == 2 && args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                DisplayMessage(client, PlayerCompanionGrind.StatusText(client.Player));
                return;
            }
            string name = args.Length >= 2 ? string.Join(" ", args, 1, args.Length - 1) : null;
            if (name != null && PlayerCompanionGrind.IsActive(client.Player))
            {
                DisplayMessage(client, PlayerCompanionGrind.SetPuller(client.Player, name));   // "/grind (name)" while grinding
                return;
            }
            PlayerCompanionGrind.TryStart(client.Player, name, out string message);
            DisplayMessage(client, message);
        }
    }
}
