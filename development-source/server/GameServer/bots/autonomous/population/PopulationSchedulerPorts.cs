using System;
using System.Collections.Generic;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Server properties of the population scheduler (owner 2026-11-08: the new system is on by default;
    /// false is the emergency switch back to the old "whole roster online" behaviour).
    /// </summary>
    public static class PopulationSchedulerProperties
    {
        public const string EnabledKey = "population_scheduler_enabled";
        public const string SavedCapKey = "population_saved_cap";

        [ServerProperty("autonomous_population", EnabledKey, "True online world: realm goals from population-goals.json, bots log in and out like players. False = the old behaviour (every saved bot online).", true)]
        public static bool POPULATION_SCHEDULER_ENABLED = true;

        [ServerProperty("autonomous_population", SavedCapKey, "Most saved (archived) bots kept. At the cap, random offline bots are retired for good down to 99% of it.", 1000000)]
        public static int POPULATION_SAVED_CAP = 1000000;
    }

    /// <summary>
    /// What a live bot is busy with, only to choose whom to log out (idle and solo first). The scheduler decides WHO is
    /// online; Bot Goals (bot-goals.json) alone decides WHAT online bots do, and the scheduler never reads or changes it.
    /// </summary>
    public enum BotEngagement : byte { Idle = 0, Fighting = 1, OnObjective = 2 }

    /// <summary>One live bot as the scheduler sees it. OnObjective = grouped, or on a Group PvE / RvR / Battleground objective.</summary>
    public readonly record struct LiveBotInfo(long BotId, int Level, BotEngagement Engagement = BotEngagement.Idle);

    /// <summary>The bot lifecycle (CORE's AutonomousBotLifecycle) as the scheduler uses it. Fakes implement it in tests.</summary>
    public interface IPopulationLifecycle
    {
        /// <summary>False until the server is open and CORE's archive and migration are ready, and again while shutting
        /// down: the scheduler then does nothing at all.</summary>
        bool Ready { get; }

        /// <summary>Adds the realm's live autonomous gamebots (never hired companions) to the list.</summary>
        void LiveBots(eRealm realm, List<LiveBotInfo> into);

        /// <summary>Online or logging in (by anyone, Horn guests too).</summary>
        bool IsOnline(long botId);
        bool RequestLogin(long botId, BotLoginPriority priority);

        /// <summary>May run later on the game loop; a refusal found there is reported through LogoutRefused.</summary>
        bool RequestLogout(long botId, BotLogoutReason reason);

        long CreateLevelOne(eRealm realm);

        event Action<long> LogoutRefused;
    }

    /// <summary>The bot archive (CORE's BotArchiveStore) as the scheduler and retirement use it.</summary>
    public interface IPopulationArchive
    {
        long SavedCount { get; }
        long Count(eRealm realm, int minLevel, int maxLevel);
        IReadOnlyList<ArchiveCandidate> Candidates(eRealm realm, int minLevel, int maxLevel, int limit, DateTime wantBeforeUtc);
        void SaveProfile(long botId, BotPopulationProfile profile);
        BotPopulationProfile GetProfile(long botId);
        IReadOnlyList<long> RandomRetireCandidates(int count, Func<ArchiveCandidate, bool> eligible);
        void Retire(IReadOnlyCollection<long> botIds);
    }

    /// <summary>Facts from outside the lifecycle: hired companions, the player's guild, Horn guests.</summary>
    public interface IPopulationWorld
    {
        bool IsHired(long botId);
        bool IsPlayerGuildmate(long botId);

        /// <summary>Bots that never count toward a goal (Horn of Rallying guests).</summary>
        bool IsGoalExempt(long botId);
    }

    public interface IPopulationClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemPopulationClock : IPopulationClock
    {
        public static readonly SystemPopulationClock Instance = new();
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
