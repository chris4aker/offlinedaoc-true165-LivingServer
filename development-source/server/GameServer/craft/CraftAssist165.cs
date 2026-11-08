using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>Op 40 state byte.</summary>
    public enum CraftAssistState : byte { Idle = 0, Running = 1, Finished = 2, Stopped = 3 }

    /// <summary>Op 40 / 41 reason byte.</summary>
    public enum CraftAssistReason : byte
    {
        None = 0, GoalReached = 1, OutOfMoney = 2, BackpackFull = 3, Interrupted = 4, NothingRaisesIt = 5,
        NoStation = 6, StoppedByPlayer = 7, Other = 8,
    }

    /// <summary>How an assistant craft ended (set by CraftAction).</summary>
    public enum CraftAssistOutcome : byte { None = 0, Made, Failed, Interrupted, StoppedByPlayer, MaterialsGone }

    /// <summary>One skill row of op 40 / 41.</summary>
    public readonly record struct CraftAssistSkillRow(byte Skill, int Now, int Projected, double EtaSeconds);

    /// <summary>One material to buy for one craft.</summary>
    public readonly record struct CraftAssistBuyLine(DbItemTemplate Material, int Packs, int Amount, long Cost);

    /// <summary>The data of one assistant run (pure: no game objects, so the state rules are testable).</summary>
    public sealed class CraftAssistRun
    {
        public readonly object Sync = new();
        public eCraftingSkill Target;
        public int Goal;
        public bool Buy, Sell;
        public CraftAssistState State = CraftAssistState.Idle;
        public CraftAssistReason Reason;
        public int StartValue, Now, Crafts;
        public long Spent, Earned;
        public ushort RecipeId;
        public long Baseline, Current;
        public double SecondsLeft;
        public long StartedAtMs, EndedAtMs;
        public List<CraftAssistSkillRow> Rows = new();
        // game side
        public ushort Region;
        public int X, Y;
        public ECSGameTimer Timer;

        public bool Running => State == CraftAssistState.Running;
        public int PointsGained => Math.Max(0, Now - StartValue);
        public long Net => Spent - Earned;

        public void Begin(eCraftingSkill target, int goal, bool buy, bool sell, int now, long nowMs)
        {
            Target = target; Goal = goal; Buy = buy; Sell = sell;
            State = CraftAssistState.Running; Reason = CraftAssistReason.None;
            StartValue = Now = now; Crafts = 0; Spent = Earned = 0; RecipeId = 0;
            Baseline = Current = 0; SecondsLeft = 0; StartedAtMs = nowMs; EndedAtMs = 0;
            Rows = new();
        }

        /// <summary>Ends a running run; false if it was not running (so a stop is reported once).</summary>
        public bool End(CraftAssistReason reason, long nowMs)
        {
            if (!Running)
                return false;
            State = reason == CraftAssistReason.GoalReached ? CraftAssistState.Finished : CraftAssistState.Stopped;
            Reason = reason;
            EndedAtMs = nowMs;
            if (reason == CraftAssistReason.GoalReached)
                SecondsLeft = 0;
            return true;
        }

        public uint ElapsedSeconds(long nowMs)
        {
            if (StartedAtMs == 0)
                return 0;
            long end = Running || EndedAtMs == 0 ? nowMs : EndedAtMs;
            return (uint)Math.Max(0, (end - StartedAtMs) / 1000);
        }
    }

    /// <summary>
    /// True 1.65 crafting assistant (owner 2026-11-08, quality of life, not in 1.65): crafts toward a skill goal on its own,
    /// re-picking the cheapest recipe per point before every craft, buying the missing materials for one craft at merchant
    /// price and selling each product at the merchant sell price. Crafting itself is untouched: every craft goes through
    /// the normal CraftItem path with the normal player timer (GMs too), real materials and the normal chances.
    /// Client protocol: crafting_overhaul_spec (ops 40 status, 41 estimate).
    /// </summary>
    public static class CraftAssist165
    {
        private static readonly ConcurrentDictionary<GamePlayer, CraftAssistRun> Runs = new();
        /// <summary>Players whose next CraftItem call is an assistant craft (set only around that call).</summary>
        private static readonly ConcurrentDictionary<GamePlayer, byte> Starting = new();

        /// <summary>Wait between one craft's end and the next craft's start (lets the client close its timer bar).</summary>
        public const int NextCraftDelayMs = 300;
        /// <summary>Walking further than this from where the run started stops it.</summary>
        public const int MaxDrift = 256;

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            GameEventMgr.AddHandler(GamePlayerEvent.Quit, new DOLEventHandler(OnPlayerQuit));
            GameEventMgr.AddHandler(GamePlayerEvent.RegionChanged, new DOLEventHandler(OnPlayerLeft));
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            GameEventMgr.RemoveHandler(GamePlayerEvent.Quit, new DOLEventHandler(OnPlayerQuit));
            GameEventMgr.RemoveHandler(GamePlayerEvent.RegionChanged, new DOLEventHandler(OnPlayerLeft));
        }

        // A quitting player's run is dropped without a word (nobody to tell).
        private static void OnPlayerQuit(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is not GamePlayer player || !Runs.TryRemove(player, out CraftAssistRun run))
                return;
            lock (run.Sync)
            {
                run.End(CraftAssistReason.Interrupted, GameLoop.GameLoopTime);
                run.Timer?.Stop();
                run.Timer = null;
            }
            Starting.TryRemove(player, out _);
        }

        // A region change stops a running assistant (death and logout are caught by the next step's checks).
        private static void OnPlayerLeft(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is GamePlayer player && IsRunning(player))
                Stop(player, CraftAssistReason.Interrupted, null);
        }

        public static bool IsRunning(GamePlayer player) => player != null && Runs.TryGetValue(player, out var r) && r.Running;

        /// <summary>True only inside the assistant's own CraftItem call (AbstractCraftingSkill.CraftItem asks).</summary>
        public static bool IsStarting(GamePlayer player) => player != null && Starting.ContainsKey(player);

        // ---- commands

        public static void Start(GamePlayer player, eCraftingSkill target, int goal, bool buy, bool sell)
        {
            CraftAssistRun run = Runs.GetOrAdd(player, _ => new CraftAssistRun());
            lock (run.Sync)
            {
                if (run.Running)
                {
                    Chat(player, "Crafting assistant is already running. Use stop first.");
                    SendStatus(player, run);
                    return;
                }
                int now = player.GetCraftingSkillValue(target);
                run.Begin(target, goal, buy, sell, now, GameLoop.GameLoopTime);
                run.Region = player.CurrentRegionID;
                run.X = player.X;
                run.Y = player.Y;

                if (player.IsCrafting)
                {
                    Chat(player, "Finish or stop your current craft first.");
                    End(player, run, CraftAssistReason.Other, null);
                    return;
                }
                if (now >= goal)
                {
                    End(player, run, CraftAssistReason.GoalReached, null);
                    return;
                }
                // a craft queue from /craftmacro would re-craft after each assistant craft: drop it for the run
                player.TempProperties.RemoveProperty(CraftAction.CRAFT_QUEUE_REMAINING_PROPERTY);

                CraftEstimate est = SafeEstimate(player, target, goal, sell);
                if (est == null || !est.Feasible)
                {
                    End(player, run, est == null || est.Reason == 0 ? CraftAssistReason.NothingRaisesIt : (CraftAssistReason)est.Reason, null);
                    return;
                }
                run.Baseline = Round(est.NetCost);
                ApplyEstimate(run, est);
                Chat(player, $"Crafting assistant started: {SkillName(target)} {now} to {goal}, estimated net cost {MoneyText(run.Baseline)}.");
                SendStatus(player, run);
                SafeStep(player, run);
            }
        }

        public static void Stop(GamePlayer player, CraftAssistReason reason, string why)
        {
            if (!Runs.TryGetValue(player, out CraftAssistRun run))
            {
                if (reason == CraftAssistReason.StoppedByPlayer)
                    SendStatus(player, new CraftAssistRun());
                return;
            }
            lock (run.Sync)
            {
                if (!run.Running)
                {
                    SendStatus(player, run);
                    return;
                }
                End(player, run, reason, why);
            }
        }

        public static void Status(GamePlayer player)
        {
            if (Runs.TryGetValue(player, out CraftAssistRun run))
                lock (run.Sync)
                    SendStatus(player, run);
            else
                SendStatus(player, new CraftAssistRun());
        }

        public static void SendEstimate(GamePlayer player, eCraftingSkill target, int goal, bool sell)
        {
            CraftEstimate est = SafeEstimate(player, target, goal, sell);
            ushort first = est == null ? (ushort)0 : est.FirstRecipeId != 0 ? est.FirstRecipeId : CraftAssist165Economics.RecipeIdOf(est.FirstRecipe);
            Hire165.Send(player, CraftAssist165Economics.OpEstimate,
                BuildEstimate((byte)target, est, player.GetCraftingSkillValue(target), goal, player.GetCurrentMoney(), first));
        }

        // ---- the loop

        // an error never leaves a run marked running
        private static void SafeStep(GamePlayer player, CraftAssistRun run)
        {
            try
            {
                Step(player, run);
            }
            catch (Exception ex)
            {
                log.Error("CraftAssist165: step failed", ex);
                End(player, run, CraftAssistReason.Other, "something went wrong.");
            }
        }

        /// <summary>Starts the next craft, or ends the run with the reason why it can't. Caller holds run.Sync.</summary>
        private static void Step(GamePlayer player, CraftAssistRun run)
        {
            if (!run.Running)
                return;
            if (!StillHere(player, run))
            {
                End(player, run, CraftAssistReason.Interrupted, null);
                return;
            }
            if (player.IsCrafting)
            {
                // a craft started by hand in the gap between two assistant crafts
                End(player, run, CraftAssistReason.StoppedByPlayer, "you started crafting by hand.");
                return;
            }

            run.Now = player.GetCraftingSkillValue(run.Target);
            if (run.Now >= run.Goal)
            {
                End(player, run, CraftAssistReason.GoalReached, null);
                return;
            }

            CraftPlanStep step;
            try
            {
                step = CraftAssist165Economics.BestRecipe(player, run.Target, CraftAssist165Economics.SkillsOf(player), run.Sell);
            }
            catch (Exception ex)
            {
                log.Error("CraftAssist165: BestRecipe failed", ex);
                step = null;
            }
            if (step?.Recipe?.Product == null)
            {
                End(player, run, CraftAssistReason.NothingRaisesIt, null);
                return;
            }

            Recipe recipe = step.Recipe;
            AbstractCraftingSkill skill = step.Skill ?? CraftingMgr.getSkillbyEnum(recipe.RequiredCraftingSkill);
            if (skill == null)
            {
                End(player, run, CraftAssistReason.NothingRaisesIt, null);
                return;
            }
            run.RecipeId = step.RecipeId != 0 ? step.RecipeId : CraftAssist165Economics.RecipeIdOf(recipe);

            // siege engines are built on the spot in the world, not in the backpack: never in an unattended loop
            if (CraftAssist165Economics.IsSiegeEngine(recipe.Product))
            {
                End(player, run, CraftAssistReason.Other, $"the best recipe now is a siege engine ({recipe.Product.Name}); build engines by hand.");
                return;
            }

            if (player.IsMoving || player.IsStrafing || player.InCombat || player.IsCasting || player.IsCrowdControlled)
            {
                End(player, run, CraftAssistReason.Interrupted, null);
                return;
            }

            // materials for ONE craft
            List<(DbItemTemplate Material, int Need)> missing = MissingMaterials(player, recipe);
            if (missing.Count > 0)
            {
                if (!run.Buy)
                {
                    skill.CheckRawMaterials(player, recipe);   // the usual missing-materials message
                    End(player, run, CraftAssistReason.Other, "materials are missing and auto-buy is off.");
                    return;
                }
                CraftAssistReason bought = BuyMissing(player, run, missing, "Crafting assistant: bought");
                if (bought != CraftAssistReason.None)
                {
                    End(player, run, bought, null);
                    return;
                }
            }

            if (!HasRoomForProduct(player, recipe))
            {
                End(player, run, CraftAssistReason.BackpackFull, null);
                return;
            }

            Starting[player] = 1;
            try
            {
                skill.CraftItem(player, recipe);
            }
            finally
            {
                Starting.TryRemove(player, out _);
            }

            if (player.craftComponent?.CraftAction?.Assist != true)
            {
                // CraftItem refused (realm rule, secondary skill, materials, moving - it said why in chat)
                End(player, run, CraftAssistReason.NoStation, null);
            }
        }

        /// <summary>CraftAction.CleanupCraftAction reports an assistant craft's end here. made = the backpack items
        /// the craft added (item, count), for the sale.</summary>
        public static void OnCraftEnded(GamePlayer player, Recipe recipe, CraftAssistOutcome outcome, List<(DbInventoryItem Item, int Count)> made)
        {
            if (player == null || !Runs.TryGetValue(player, out CraftAssistRun run))
                return;
            lock (run.Sync)
            {
                if (!run.Running)
                    return;
                try
                {
                    AfterCraft(player, run, outcome, made);
                }
                catch (Exception ex)
                {
                    log.Error("CraftAssist165: after-craft failed", ex);
                    End(player, run, CraftAssistReason.Other, "something went wrong.");
                }
            }
        }

        // counts the craft, sells the product, then stops or schedules the next craft (caller holds run.Sync)
        private static void AfterCraft(GamePlayer player, CraftAssistRun run, CraftAssistOutcome outcome, List<(DbInventoryItem Item, int Count)> made)
        {
            CraftAssistReason stop = ReasonFor(outcome);
            if (outcome is CraftAssistOutcome.Made or CraftAssistOutcome.Failed)
                run.Crafts++;
            if (outcome == CraftAssistOutcome.Made && run.Sell && made != null)
                SellMade(player, run, made);

            run.Now = player.GetCraftingSkillValue(run.Target);
            if (stop != CraftAssistReason.None)
            {
                End(player, run, stop, outcome == CraftAssistOutcome.MaterialsGone ? "the materials were gone when the craft finished." : null);
                return;
            }
            if (run.Now >= run.Goal)
            {
                End(player, run, CraftAssistReason.GoalReached, null);
                return;
            }

            CraftEstimate est = SafeEstimate(player, run.Target, run.Goal, run.Sell);
            if (est != null && est.Feasible)
                ApplyEstimate(run, est);
            SendStatus(player, run);

            // the next craft runs from the timer service, after this craft's cleanup is complete
            run.Timer?.Stop();
            run.Timer = new ECSGameTimer(player, t =>
            {
                lock (run.Sync)
                {
                    if (run.Timer == t)
                        run.Timer = null;
                    SafeStep(player, run);
                }
                return 0;
            }, NextCraftDelayMs);
        }

        /// <summary>Maps a craft outcome to the stop reason it causes (None = keep going).</summary>
        public static CraftAssistReason ReasonFor(CraftAssistOutcome outcome) => outcome switch
        {
            CraftAssistOutcome.Made or CraftAssistOutcome.Failed => CraftAssistReason.None,
            CraftAssistOutcome.StoppedByPlayer => CraftAssistReason.StoppedByPlayer,
            CraftAssistOutcome.MaterialsGone => CraftAssistReason.Other,
            _ => CraftAssistReason.Interrupted,
        };

        /// <summary>Ends the run: stops the craft in progress (no materials lost), tells the player, sends op 40.</summary>
        private static void End(GamePlayer player, CraftAssistRun run, CraftAssistReason reason, string why)
        {
            if (!run.End(reason, GameLoop.GameLoopTime))
                return;
            run.Timer?.Stop();
            run.Timer = null;
            run.Now = player.GetCraftingSkillValue(run.Target);
            if (reason == CraftAssistReason.GoalReached)
                run.Current = run.Net;
            // the run is no longer running, so the craft's cleanup report is ignored
            if (player.craftComponent?.CraftAction?.Assist == true)
                player.craftComponent.StopCraft();

            if (reason == CraftAssistReason.GoalReached)
                Chat(player, $"Crafting assistant finished: {SkillName(run.Target)} reached {run.Now}.");
            else
                Chat(player, $"Crafting assistant stopped: {why ?? ReasonText(reason)}");
            Chat(player, $"Crafting assistant: {run.Crafts} crafts, {run.PointsGained} points, spent {MoneyText(run.Spent)}, earned {MoneyText(run.Earned)}.");
            SendStatus(player, run);
        }

        private static bool StillHere(GamePlayer player, CraftAssistRun run)
        {
            if (player?.Client == null || player.ObjectState != GameObject.eObjectState.Active || !player.IsAlive)
                return false;
            if (player.Client.ClientState != GameClient.eClientState.Playing)
                return false;
            if (player.CurrentRegionID != run.Region)
                return false;
            long dx = player.X - run.X, dy = player.Y - run.Y;
            return dx * dx + dy * dy <= (long)MaxDrift * MaxDrift;
        }

        // ---- buying

        /// <summary>The backpack's materials short of one craft (matched by name, as CheckRawMaterials does).</summary>
        private static List<(DbItemTemplate Material, int Need)> MissingMaterials(GamePlayer player, Recipe recipe)
        {
            var result = new List<(DbItemTemplate, int)>();
            lock (player.Inventory.Lock)
            {
                var have = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (DbInventoryItem item in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                    if (item?.Name != null)
                        have[item.Name] = have.GetValueOrDefault(item.Name) + item.Count;
                foreach (Ingredient ingredient in recipe.Ingredients)
                {
                    DbItemTemplate material = ingredient.Material;
                    int got = have.GetValueOrDefault(material.Name);
                    int use = Math.Min(got, ingredient.Count);
                    have[material.Name] = got - use;
                    if (ingredient.Count > use)
                        result.Add((material, ingredient.Count - use));
                }
            }
            return result;
        }

        /// <summary>Buy lines for the missing amounts at merchant price: whole packs of the material's pack size, each
        /// pack at the template price (what a merchant, and /craftmacro buy, charges).</summary>
        public static List<CraftAssistBuyLine> PlanBuy(IEnumerable<(DbItemTemplate Material, int Need)> missing)
        {
            var lines = new List<CraftAssistBuyLine>();
            foreach (var (material, need) in missing)
            {
                if (need <= 0)
                    continue;
                int pack = Math.Max(1, material.PackSize);
                int packs = (need + pack - 1) / pack;
                lines.Add(new CraftAssistBuyLine(material, packs, packs * pack, packs * material.Price));
            }
            return lines;
        }

        /// <summary>The MAKE button (players only, not bots): the missing materials of one craft are bought at merchant
        /// prices first when the player can afford them (owner 2026-11-08). Returns false (with a chat line) when they
        /// can't be afforded or carried - the craft is then refused. Materials no merchant sells are left to the normal
        /// missing-materials check.</summary>
        public static bool AutoBuyForMake(GamePlayer player, Recipe recipe)
        {
            if (player?.Inventory == null || recipe == null)
                return true;
            List<(DbItemTemplate Material, int Need)> missing = MissingMaterials(player, recipe);
            if (missing.Count == 0)
                return true;
            List<CraftAssistBuyLine> lines = PlanBuy(missing);
            if (lines.Any(l => l.Material.Price <= 0))
                return true;
            long total = lines.Sum(l => l.Cost);
            if (total > player.GetCurrentMoney())
            {
                Chat(player, $"You can't afford the missing materials for {recipe.Product.Name} ({MoneyText(total)}).");
                return false;
            }
            CraftAssistReason result = BuyMissing(player, null, missing, "You buy");
            if (result == CraftAssistReason.BackpackFull)
                Chat(player, "Your backpack has no room for the missing materials.");
            return result == CraftAssistReason.None;
        }

        private static CraftAssistReason BuyMissing(GamePlayer player, CraftAssistRun run, List<(DbItemTemplate Material, int Need)> missing, string boughtText)
        {
            List<CraftAssistBuyLine> lines = PlanBuy(missing);
            CraftAssistBuyLine unbuyable = lines.FirstOrDefault(l => l.Material.Price <= 0);
            if (unbuyable.Material != null)
            {
                Chat(player, $"Crafting assistant: {unbuyable.Material.Name} can't be bought from a merchant.");
                return CraftAssistReason.Other;
            }
            long total = lines.Sum(l => l.Cost);
            if (total > player.GetCurrentMoney())
                return CraftAssistReason.OutOfMoney;

            var bought = new List<string>();
            long paid = 0;
            CraftAssistReason result = CraftAssistReason.None;
            foreach (CraftAssistBuyLine line in lines)
            {
                if (!player.Inventory.AddTemplate(GameInventoryItem.Create(line.Material), line.Amount, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                {
                    result = CraftAssistReason.BackpackFull;
                    break;
                }
                if (!player.RemoveMoney(line.Cost))
                {
                    // money changed meanwhile: take the goods back
                    RemoveByTemplate(player, line.Material, line.Amount);
                    result = CraftAssistReason.OutOfMoney;
                    break;
                }
                paid += line.Cost;
                bought.Add($"{line.Amount} {Plural(line.Material.Name, line.Amount)}");
            }
            if (run != null)
                run.Spent += paid;
            if (bought.Count > 0)
                Chat(player, $"{boughtText} {JoinAnd(bought)} for {MoneyText(paid)}.");
            return result;
        }

        private static void RemoveByTemplate(GamePlayer player, DbItemTemplate template, int amount)
        {
            foreach (DbInventoryItem item in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack).ToList())
            {
                if (amount <= 0)
                    break;
                if (item == null || item.Id_nb != template.Id_nb)
                    continue;
                int take = Math.Min(amount, item.Count);
                if (take == item.Count)
                    player.Inventory.RemoveItem(item);
                else
                    player.Inventory.RemoveCountFromStack(item, take);
                amount -= take;
            }
        }

        /// <summary>Room for the product: a free backpack slot, or (stackable, not unique) room on a stack of it.</summary>
        private static bool HasRoomForProduct(GamePlayer player, Recipe recipe)
        {
            DbItemTemplate product = recipe.Product;
            lock (player.Inventory.Lock)
            {
                if (player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) != eInventorySlot.Invalid)
                    return true;
                if (recipe.IsForUniqueProduct)
                    return false;
                int count = Math.Max(1, product.PackSize);
                int free = 0;
                foreach (DbInventoryItem item in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                    if (item != null && item.Id_nb == product.Id_nb)
                        free += Math.Max(0, item.MaxCount - item.Count);
                return free >= count;
            }
        }

        // ---- selling

        /// <summary>What a merchant pays for count pieces (GameMerchant.OnPlayerAppraise's formula).</summary>
        public static long MerchantSellValue(long price, int count, int packSize, int sellRatio, bool canSell)
        {
            if (!canSell)
                return 0;
            count = Math.Max(1, count);
            packSize = Math.Max(1, packSize);
            long value = price * count / packSize * sellRatio / 100;
            if (price == 1 && value == 0)
                value = price * count / packSize;
            return Math.Max(0, value);
        }

        private static void SellMade(GamePlayer player, CraftAssistRun run, List<(DbInventoryItem Item, int Count)> made)
        {
            foreach (var (item, count) in made)
            {
                if (item == null || count <= 0)
                    continue;
                long value = MerchantSellValue(item.Price, count, item.PackSize, ServerProperties.Properties.ITEM_SELL_RATIO,
                    FrontierSupplyMerchantPolicy.CanSell(item));
                if (value <= 0)
                    continue;   // nobody buys it: it stays in the backpack
                string name = item.Name;
                bool removed = count >= item.Count ? player.Inventory.RemoveItem(item) : player.Inventory.RemoveCountFromStack(item, count);
                if (!removed)
                    continue;
                player.AddMoney(value);
                run.Earned += value;
                string what = count > 1 ? $"{count} {Plural(name, count)}" : name;
                Chat(player, $"Crafting assistant: sold {what} for {MoneyText(value)}.");
            }
        }

        /// <summary>The backpack's items and counts, before a craft builds its product.</summary>
        public static Dictionary<DbInventoryItem, int> SnapshotBackpack(GamePlayer player)
        {
            var snap = new Dictionary<DbInventoryItem, int>(ReferenceEqualityComparer.Instance);
            lock (player.Inventory.Lock)
                foreach (DbInventoryItem item in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                    if (item != null)
                        snap[item] = item.Count;
            return snap;
        }

        /// <summary>The backpack items added since the snapshot (new items, or stacks that grew) with the added count.</summary>
        public static List<(DbInventoryItem Item, int Count)> AddedSince(GamePlayer player, Dictionary<DbInventoryItem, int> before)
        {
            var added = new List<(DbInventoryItem, int)>();
            lock (player.Inventory.Lock)
                foreach (DbInventoryItem item in player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack))
                {
                    if (item == null)
                        continue;
                    int was = before.TryGetValue(item, out int c) ? c : 0;
                    if (item.Count > was)
                        added.Add((item, item.Count - was));
                }
            return added;
        }

        // ---- estimates

        private static CraftEstimate SafeEstimate(GamePlayer player, eCraftingSkill target, int goal, bool sell)
        {
            try
            {
                return CraftAssist165Economics.Estimate(player, target, goal, sell);
            }
            catch (Exception ex)
            {
                log.Error("CraftAssist165: Estimate failed", ex);
                return null;
            }
        }

        /// <summary>Current = net so far + expected net of the rest; seconds left and skill rows from the estimate.</summary>
        public static void ApplyEstimate(CraftAssistRun run, CraftEstimate est)
        {
            run.Current = run.Net + Round(est.NetCost);
            run.SecondsLeft = Math.Max(0, est.Seconds);
            run.Rows = RowsOf(est);
        }

        public static List<CraftAssistSkillRow> RowsOf(CraftEstimate est)
        {
            var rows = new List<CraftAssistSkillRow>();
            if (est?.Skills == null)
                return rows;
            foreach (var s in est.Skills)
            {
                rows.Add(new CraftAssistSkillRow((byte)s.Skill, s.Now, s.Projected, s.EtaSeconds));
                if (rows.Count == 8)
                    break;
            }
            return rows;
        }

        // ---- packets (pure: payload bytes from body index 4)

        public static void SendStatus(GamePlayer player, CraftAssistRun run) =>
            Hire165.Send(player, CraftAssist165Economics.OpStatus, BuildStatus(run, GameLoop.GameLoopTime));

        /// <summary>Op 40 payload; index 0 here is body [4].</summary>
        public static byte[] BuildStatus(CraftAssistRun run, long nowMs)
        {
            int n = Math.Min(8, run.Rows?.Count ?? 0);
            byte[] p = new byte[39 + n * 9];
            p[0] = (byte)run.State;
            p[1] = (byte)run.Reason;
            p[2] = (byte)run.Target;
            p[3] = (byte)((run.Buy ? 1 : 0) | (run.Sell ? 2 : 0));
            PutU16(p, 4, run.Goal);
            PutU16(p, 6, run.Now);
            PutU16(p, 8, run.Crafts);
            PutU16(p, 10, run.PointsGained);
            PutU32(p, 12, run.Spent);
            PutU32(p, 16, run.Earned);
            PutU16(p, 20, run.RecipeId);
            PutI32(p, 22, run.Baseline);
            PutI32(p, 26, run.Current);
            PutU32(p, 30, (long)Math.Round(run.SecondsLeft));
            PutU32(p, 34, run.ElapsedSeconds(nowMs));
            p[38] = (byte)n;
            for (int i = 0; i < n; i++)
                PutRow(p, 39 + i * 9, run.Rows[i]);
            return p;
        }

        /// <summary>Op 41 payload; index 0 here is body [4]. A null estimate is reported as not feasible.</summary>
        public static byte[] BuildEstimate(byte target, CraftEstimate est, int from, int goal, long money, ushort firstRecipeId)
        {
            List<CraftAssistSkillRow> rows = RowsOf(est);
            byte[] p = new byte[35 + rows.Count * 9];
            p[0] = target;
            p[1] = (byte)(est?.Feasible == true ? 1 : 0);
            p[2] = est == null ? (byte)CraftAssistReason.Other : est.Feasible ? (byte)0 : est.Reason;
            p[3] = 0;
            PutU16(p, 4, est?.From > 0 ? est.From : from);
            PutU16(p, 6, est?.Goal > 0 ? est.Goal : goal);
            PutU32(p, 8, (long)Math.Round(est?.Crafts ?? 0));
            PutU32(p, 12, (long)Math.Round(est?.Seconds ?? 0));
            PutU32(p, 16, (long)Math.Round(est?.MaterialCost ?? 0));
            PutU32(p, 20, (long)Math.Round(est?.Resale ?? 0));
            PutI32(p, 24, Round(est?.NetCost ?? 0));
            PutU32(p, 28, money);
            PutU16(p, 32, firstRecipeId);
            p[34] = (byte)rows.Count;
            for (int i = 0; i < rows.Count; i++)
                PutRow(p, 35 + i * 9, rows[i]);
            return p;
        }

        private static void PutRow(byte[] p, int at, CraftAssistSkillRow row)
        {
            p[at] = row.Skill;
            PutU16(p, at + 1, row.Now);
            PutU16(p, at + 3, row.Projected);
            PutU32(p, at + 5, (long)Math.Round(Math.Max(0, row.EtaSeconds)));
        }

        private static void PutU16(byte[] p, int at, long v)
        {
            ushort u = (ushort)Math.Clamp(v, 0, ushort.MaxValue);
            p[at] = (byte)u;
            p[at + 1] = (byte)(u >> 8);
        }

        private static void PutU32(byte[] p, int at, long v)
        {
            uint u = (uint)Math.Clamp(v, 0, uint.MaxValue);
            for (int i = 0; i < 4; i++)
                p[at + i] = (byte)(u >> (8 * i));
        }

        private static void PutI32(byte[] p, int at, long v)
        {
            int s = (int)Math.Clamp(v, int.MinValue, int.MaxValue);
            for (int i = 0; i < 4; i++)
                p[at + i] = (byte)(s >> (8 * i));
        }

        private static long Round(double v) => double.IsFinite(v) ? (long)Math.Round(v) : 0;

        // ---- helpers

        public static string ReasonText(CraftAssistReason reason) => reason switch
        {
            CraftAssistReason.GoalReached => "goal reached.",
            CraftAssistReason.OutOfMoney => "not enough money for the materials.",
            CraftAssistReason.BackpackFull => "your backpack is full.",
            CraftAssistReason.Interrupted => "interrupted (you moved, left, were attacked or were stunned).",
            CraftAssistReason.NothingRaisesIt => "no recipe can raise that skill now.",
            CraftAssistReason.NoStation => "the craft could not start (see the message above).",
            CraftAssistReason.StoppedByPlayer => "stopped by you.",
            _ => "the craft could not continue.",
        };

        public static string SkillName(eCraftingSkill skill) => CraftingMgr.getSkillbyEnum(skill)?.Name ?? skill.ToString();

        private static string MoneyText(long copper) => copper < 0 ? "-" + Money.GetString(-copper) : Money.GetString(copper);

        /// <summary>Plain English plural for chat lines ("cloth square" -> "cloth squares").</summary>
        public static string Plural(string name, int count)
        {
            if (count == 1 || string.IsNullOrEmpty(name) || name.EndsWith('s'))
                return name;
            if (name.EndsWith('y') && name.Length > 1 && "aeiou".IndexOf(name[^2]) < 0)
                return name[..^1] + "ies";
            if (name.EndsWith("ch") || name.EndsWith("sh") || name.EndsWith('x'))
                return name + "es";
            return name + "s";
        }

        private static string JoinAnd(List<string> parts) =>
            parts.Count <= 1 ? string.Concat(parts) : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];

        private static void Chat(GamePlayer player, string text) =>
            player?.Out?.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);

        private static readonly Logging.Logger log = Logging.LoggerManager.Create(typeof(CraftAssist165));
    }
}
