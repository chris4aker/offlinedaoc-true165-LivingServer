using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Coalesces launcher/status persistence away from NPC think threads. Hundreds
/// of bots can change their in-memory activity every second, but SQLite sees a
/// compact batch instead of one synchronous transaction per AI turn.
/// </summary>
public static class AutonomousBotStatusPersistence
{
    // Same sustained throughput as the original 32 records every two seconds,
    // but each batch holds DatabaseWriteLock half as long. Game-loop exchange
    // and inventory transactions wait on that lock, so shorter holds mean
    // shorter worst-case AI turns.
    private const int WriteBatchSize = 16;
    private const int FlushIntervalMs = 1_000;
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBotStatusPersistence));
    private static readonly object PendingGate = new();
    private static readonly Dictionary<long, GameBot> Pending = new();
    private static readonly Queue<long> PendingOrder = new();
    // Ids already waiting in PendingOrder. A bot saved through the inventory or priority queue left its id behind in
    // PendingOrder and was enqueued again on its next update: 36,545 ids for ~6,000 bots, +3,300 every five minutes
    // (SERVER_STATIC_CENSUS, 2026-10-15).
    private static readonly HashSet<long> InPendingOrder = new();
    private static readonly HashSet<long> PendingPriority = new();
    private static readonly Queue<long> PendingPriorityOrder = new();
    private static readonly HashSet<long> PendingInventory = new();
    private static readonly Queue<long> PendingInventoryOrder = new();
    // Logout saves (record + inventory in one lock hold), ahead of everything else. The sequence number lets a money or
    // item change that arrives while the save is in flight queue it again instead of being lost.
    private static readonly Dictionary<long, GameBot> LogoutBots = new();
    private static readonly Dictionary<long, int> LogoutSequence = new();
    private static readonly Queue<long> LogoutOrder = new();
    private static readonly HashSet<long> InLogoutOrder = new();
    private static readonly Timer FlushTimer = new(_ => Flush(), null, FlushIntervalMs, FlushIntervalMs);
    private static int _flushing;

    public static object DatabaseWriteLock { get; } = new();

    /// <summary>Raised on the flush thread after a bot's logout save is written (and no newer change waits).</summary>
    public static event Action<GameBot> LogoutSaved;

    public static void Queue(GameBot bot, bool includeInventory = false)
    {
        if (bot?.IsAutonomousWorldBot == true && bot.DatabaseID > 0 && bot.PersistentRecord != null)
        {
            lock (PendingGate)
            {
                // A bot that left the world saves through its logout entry only.
                if (LogoutBots.TryGetValue(bot.DatabaseID, out GameBot leaving) && ReferenceEquals(leaving, bot))
                {
                    QueueLogoutLocked(bot);
                    return;
                }
                if (IsStaleInstance(bot))
                    return;
                if (InPendingOrder.Add(bot.DatabaseID))
                    PendingOrder.Enqueue(bot.DatabaseID);
                Pending[bot.DatabaseID] = bot;
                if (includeInventory && PendingInventory.Add(bot.DatabaseID))
                    PendingInventoryOrder.Enqueue(bot.DatabaseID);
            }
        }
    }

    /// <summary>An old copy of a bot that logged in again as a new instance: its late saves would overwrite newer progress.</summary>
    private static bool IsStaleInstance(GameBot bot) =>
        bot.ObjectState != GameObject.eObjectState.Active &&
        AutonomousBotRegistry.TryGet(bot.DatabaseID, out GameBot current) && current != null && !ReferenceEquals(current, bot);

    /// <summary>
    /// The one save of a bot that left the world: record and inventory together, at the front of the queue. Any routine
    /// entry for it is dropped so an older snapshot can never be written after it.
    /// </summary>
    public static void QueueLogout(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.DatabaseID <= 0 || bot.PersistentRecord == null)
            return;
        lock (PendingGate)
            QueueLogoutLocked(bot);
    }

    private static void QueueLogoutLocked(GameBot bot)
    {
        long botId = bot.DatabaseID;
        LogoutBots[botId] = bot;
        LogoutSequence[botId] = LogoutSequence.TryGetValue(botId, out int sequence) ? sequence + 1 : 1;
        if (InLogoutOrder.Add(botId))
            LogoutOrder.Enqueue(botId);
        Pending.Remove(botId);
        PendingPriority.Remove(botId);
        PendingInventory.Remove(botId);
    }

    /// <summary>A logout save for this bot is still waiting: it must not log in again until the save is written.</summary>
    public static bool IsLogoutPending(long botId)
    {
        lock (PendingGate)
            return LogoutBots.ContainsKey(botId);
    }

    /// <summary>The bot whose logout save is waiting (its record is the newest copy of its money).</summary>
    public static bool TryGetLoggingOut(long botId, out GameBot bot)
    {
        lock (PendingGate)
            return LogoutBots.TryGetValue(botId, out bot);
    }

    public static int PendingLogoutSaves
    {
        get { lock (PendingGate) return LogoutBots.Count; }
    }

    /// <summary>A bot deleted for good leaves the save queue (2026-10-30: a released companion's record was gone, its
    /// failed save requeued the whole batch every second, 918 times, and held up the other bots' saves).</summary>
    public static void Forget(long botId)
    {
        lock (PendingGate)
        {
            Pending.Remove(botId);
            PendingPriority.Remove(botId);
            PendingInventory.Remove(botId);
            LogoutBots.Remove(botId);
            LogoutSequence.Remove(botId);
        }
    }

    /// <summary>
    /// Group membership is shown as one launcher card and must not sit behind
    /// thousands of routine activity updates. Priority affects only status-row
    /// ordering; it does not bypass the same bounded batch or database lock.
    /// </summary>
    public static void QueueGroupMetadata(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.DatabaseID <= 0 || bot.PersistentRecord == null)
            return;
        lock (PendingGate)
        {
            if (LogoutBots.TryGetValue(bot.DatabaseID, out GameBot leaving) && ReferenceEquals(leaving, bot))
            {
                QueueLogoutLocked(bot);
                return;
            }
            if (IsStaleInstance(bot))
                return;
            if (InPendingOrder.Add(bot.DatabaseID))
                PendingOrder.Enqueue(bot.DatabaseID);
            Pending[bot.DatabaseID] = bot;
            if (PendingPriority.Add(bot.DatabaseID))
                PendingPriorityOrder.Enqueue(bot.DatabaseID);
        }
    }

    private static int PendingCount
    {
        get { lock (PendingGate) return Pending.Count + LogoutBots.Count; }
    }

    /// <summary>
    /// A bot that just logged in must not wait behind routine updates: the
    /// launcher only teleports to bots whose saved row says they are online.
    /// </summary>
    public static void QueueLogin(GameBot bot) => QueueGroupMetadata(bot);

    /// <summary>
    /// Inventory and group changes are durable first, but they may fill at most
    /// this much of a batch. With thousands of bots looting, they used to take
    /// every slot every second, so bots whose inventory rarely changed (healers,
    /// full backpacks) went an hour or more without a saved status.
    /// </summary>
    public const int MaximumNonRoutinePerBatch = WriteBatchSize - 4;

    private const long DiagnosticIntervalTicks = 300_000;
    private static long _nextDiagnosticTick;
    private static int _savedSinceDiagnostic;

    public static int Flush()
    {
        if (Interlocked.Exchange(ref _flushing, 1) != 0)
            return 0;

        try
        {
            // One bounded batch per second. A second "catch-up" batch was tried
            // and doubled database lock time; with 6,000 bots the game loop
            // stalled for over a second at a time, so throughput stays as it was.
            int saved = SaveBatch();

            Interlocked.Add(ref _savedSinceDiagnostic, saved);
            LogBacklogPeriodically();
            return saved;
        }
        finally
        {
            Volatile.Write(ref _flushing, 0);
        }
    }

    /// <summary>
    /// Picks one batch. A normal batch takes inventory changes, then group or
    /// login changes, up to <see cref="MaximumNonRoutinePerBatch"/>, then the
    /// oldest routine updates, then fills any space left from the first two.
    /// </summary>
    private static int SaveBatch()
    {
        var drained = new Dictionary<long, GameBot>(WriteBatchSize);
        var inventoryIds = new HashSet<long>();
        var priorityIds = new HashSet<long>();
        var logoutSequences = new Dictionary<long, int>();
        try
        {
            lock (PendingGate)
            {
                while (drained.Count < MaximumNonRoutinePerBatch && LogoutOrder.Count > 0)
                {
                    long botId = LogoutOrder.Dequeue();
                    InLogoutOrder.Remove(botId);
                    if (!LogoutBots.TryGetValue(botId, out GameBot leaving))
                        continue;
                    drained[botId] = leaving;
                    inventoryIds.Add(botId);
                    logoutSequences[botId] = LogoutSequence.GetValueOrDefault(botId);
                }
                DrainInventory(drained, inventoryIds, MaximumNonRoutinePerBatch);
                DrainPriority(drained, inventoryIds, priorityIds, MaximumNonRoutinePerBatch);

                while (drained.Count < WriteBatchSize && PendingOrder.Count > 0)
                {
                    long botId = PendingOrder.Dequeue();
                    InPendingOrder.Remove(botId);
                    if (!Pending.Remove(botId, out GameBot bot))
                        continue;
                    drained[botId] = bot;
                    PendingPriority.Remove(botId);
                    if (PendingInventory.Remove(botId))
                        inventoryIds.Add(botId);
                }

                DrainInventory(drained, inventoryIds, WriteBatchSize);
                DrainPriority(drained, inventoryIds, priorityIds, WriteBatchSize);
            }

            GameBot[] bots = drained.Values.ToArray();
            if (bots.Length == 0)
                return 0;

            (GameBot Bot, OfflineWorldBotRecord Record)[] snapshots = bots
                .Where(bot => bot?.PersistentRecord?.IsPersisted == true)
                .Select(bot => (bot, bot.PrepareAutonomousStateSnapshot()))
                .Where(entry => entry.Item2 != null)
                .ToArray();
            // A leaving bot that can't be saved this way (record never stored) must not stay "logout pending" forever.
            foreach (long botId in logoutSequences.Keys)
                if (!snapshots.Any(entry => entry.Bot.DatabaseID == botId))
                    Forget(botId);
            if (snapshots.Length == 0)
                return 0;

            int savedCount = 0;
            foreach ((GameBot Bot, OfflineWorldBotRecord Record)[] batch in snapshots.Chunk(WriteBatchSize))
            {
                bool saved;
                lock (DatabaseWriteLock)
                {
                    saved = GameServer.Database.SaveObject(batch.Select(entry => (DataObject)entry.Record));
                    if (saved)
                    {
                        saved = BotInventory.SaveManyIntoDatabase(batch
                            .Where(entry => inventoryIds.Contains(entry.Bot.DatabaseID) && entry.Bot.Inventory is BotInventory)
                            .Select(entry => ((BotInventory)entry.Bot.Inventory, entry.Bot.InternalID)));
                    }
                }

                if (!saved)
                {
                    foreach ((GameBot bot, _) in batch)
                    {
                        // a bot whose row was deleted can never save: drop it instead of retrying the batch forever
                        if (DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("BotId").IsEqualTo(bot.DatabaseID)) == null)
                        {
                            Log.Warn($"AUTONOMOUS_STATUS_SAVE_DROPPED bot=\"{bot.Name}\" id={bot.DatabaseID}: its record no longer exists");
                            if (logoutSequences.ContainsKey(bot.DatabaseID))
                                Forget(bot.DatabaseID);
                            continue;
                        }
                        if (logoutSequences.ContainsKey(bot.DatabaseID))
                        {
                            RequeueLogout(bot.DatabaseID);
                            continue;
                        }
                        Queue(bot, inventoryIds.Contains(bot.DatabaseID));
                        if (priorityIds.Contains(bot.DatabaseID))
                            QueueGroupMetadata(bot);
                    }
                    continue;
                }

                foreach ((GameBot bot, _) in batch)
                {
                    bot.MarkAutonomousStateSaved();
                    if (logoutSequences.TryGetValue(bot.DatabaseID, out int sequence))
                        CompleteLogout(bot, sequence);
                }
                savedCount += batch.Length;
            }

            return savedCount;
        }
        catch (Exception exception)
        {
            foreach ((long botId, GameBot bot) in drained)
            {
                if (logoutSequences.ContainsKey(botId))
                {
                    RequeueLogout(botId);
                    continue;
                }
                Queue(bot, inventoryIds.Contains(botId));
                if (priorityIds.Contains(botId))
                    QueueGroupMetadata(bot);
            }
            Log.Error("Autonomous status batch persistence failed", exception);
            return 0;
        }
    }

    private static void RequeueLogout(long botId)
    {
        lock (PendingGate)
            if (LogoutBots.ContainsKey(botId) && InLogoutOrder.Add(botId))
                LogoutOrder.Enqueue(botId);
    }

    /// <summary>The logout save is written: forget the bot unless a newer change queued it again meanwhile.</summary>
    private static void CompleteLogout(GameBot bot, int savedSequence)
    {
        long botId = bot.DatabaseID;
        lock (PendingGate)
        {
            if (!LogoutBots.TryGetValue(botId, out GameBot leaving) || !ReferenceEquals(leaving, bot) ||
                LogoutSequence.GetValueOrDefault(botId) != savedSequence)
                return;
            LogoutBots.Remove(botId);
            LogoutSequence.Remove(botId);
            Pending.Remove(botId);
            PendingPriority.Remove(botId);
            PendingInventory.Remove(botId);
        }
        try
        {
            LogoutSaved?.Invoke(bot);
        }
        catch (Exception exception)
        {
            Log.Warn($"AUTONOMOUS_LOGOUT_SAVED handler failed for {botId}: {exception.Message}");
        }
    }

    // Callers hold PendingGate. Stale ids are harmless: they remain in an
    // order queue only until dequeued.
    private static void DrainInventory(Dictionary<long, GameBot> drained, HashSet<long> inventoryIds, int limit)
    {
        while (drained.Count < limit && PendingInventoryOrder.Count > 0)
        {
            long botId = PendingInventoryOrder.Dequeue();
            PendingInventory.Remove(botId);
            if (Pending.Remove(botId, out GameBot bot))
            {
                drained[botId] = bot;
                inventoryIds.Add(botId);
            }
        }
    }

    private static void DrainPriority(Dictionary<long, GameBot> drained, HashSet<long> inventoryIds,
        HashSet<long> priorityIds, int limit)
    {
        while (drained.Count < limit && PendingPriorityOrder.Count > 0)
        {
            long botId = PendingPriorityOrder.Dequeue();
            PendingPriority.Remove(botId);
            if (Pending.Remove(botId, out GameBot bot))
            {
                drained[botId] = bot;
                priorityIds.Add(botId);
                if (PendingInventory.Remove(botId))
                    inventoryIds.Add(botId);
            }
        }
    }

    /// <summary>Every five minutes: how many bots are waiting and how many were saved.</summary>
    private static void LogBacklogPeriodically()
    {
        long now = GameLoop.GameLoopTime;
        if (now < _nextDiagnosticTick)
            return;

        _nextDiagnosticTick = now + DiagnosticIntervalTicks;
        int pending, inventory, priority;
        lock (PendingGate)
        {
            pending = Pending.Count;
            inventory = PendingInventory.Count;
            priority = PendingPriority.Count;
        }

        int saved = Interlocked.Exchange(ref _savedSinceDiagnostic, 0);
        if (saved == 0 && pending == 0)
            return;

        // This runs on the flush timer thread, where an exception would end
        // the process; a diagnostic line must never be able to do that.
        try
        {
            Log.Info($"AUTONOMOUS_STATUS_PERSISTENCE pending={pending} inventory={inventory} priority={priority} saved_last_5m={saved}");
        }
        catch
        {
        }
    }

    /// <summary>
    /// Server shutdown is the one place where durability outranks pacing. Wait
    /// for an in-flight timer flush and drain every remaining batch before the
    /// database is closed. Normal runtime flushes remain bounded to one batch.
    /// </summary>
    public static int FlushAll()
    {
        int saved = 0;
        int stalledAttempts = 0;
        while (Volatile.Read(ref _flushing) != 0 || PendingCount > 0)
        {
            if (Volatile.Read(ref _flushing) != 0)
            {
                Thread.Sleep(10);
                continue;
            }

            int before = PendingCount;
            saved += Flush();
            int after = PendingCount;
            if (after >= before && after > 0)
            {
                if (++stalledAttempts >= 3)
                {
                    Log.Error($"Autonomous shutdown persistence stopped after three failed batches; {after} bots remain queued.");
                    break;
                }
                Thread.Sleep(50);
            }
            else
            {
                stalledAttempts = 0;
            }
        }

        return saved;
    }
}
