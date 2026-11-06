using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.GS.Quests;

namespace DOL.GS
{
    /// <summary>
    /// True 1.65 Mode: a classic quest's item drops from a monster's loot only for a player who is on a quest that uses it
    /// (owner 2026-11-06: "why do quest items drop even when im not on the quest? is that period behavior?" - it is not:
    /// in 1.65 a collect item dropped only while you were on the quest; and "bots can end up stuck with these untradable
    /// quest items too"). The quest generator put some of them (Golden Chain, bandit's ear, rat tail...) in plain loot
    /// tables, so they fell for everyone, gamebots included, who can neither use, sell nor trade them. Now such a drop
    /// needs a player among the loot owners (the killer, or a player in the killing group) with one of the quests that
    /// names the item active; gamebots and companions never get one on their own.
    /// </summary>
    public static class QuestLootGate165
    {
        public const string QuestPackage = "ClassicQuest";
        private static readonly Regex QuestItemId = new(@"cq_[A-Za-z0-9_]+", RegexOptions.Compiled);
        private static readonly object Sync = new();
        private static Dictionary<string, HashSet<int>> _questsByItem;

        public static bool IsQuestItem(DbItemTemplate item) => item != null &&
            (string.Equals(item.PackageID, QuestPackage, StringComparison.Ordinal) ||
             item.Id_nb?.StartsWith("cq_", StringComparison.OrdinalIgnoreCase) == true);

        /// <summary>Whether this loot may drop: anything that is not a quest item, or a quest item a loot owner's quest uses.</summary>
        public static bool MayDrop(DbItemTemplate item, IEnumerable<IGameStaticItemOwner> owners)
        {
            if (!Classic165Mode.IsOn || !IsQuestItem(item))
                return true;
            if (!QuestsByItem().TryGetValue(item.Id_nb.ToLowerInvariant(), out HashSet<int> quests))
                return false;
            foreach (IGameStaticItemOwner owner in owners ?? [])
                foreach (GamePlayer player in Players(owner))
                    if (OnAnyQuest(player, quests))
                        return true;
            return false;
        }

        public static bool OnAnyQuest(GamePlayer player, HashSet<int> quests) =>
            player?.QuestList != null && player.QuestList.Keys.OfType<DataQuest>().Any(quest => quests.Contains(quest.ID));

        private static IEnumerable<GamePlayer> Players(IGameStaticItemOwner owner) => owner switch
        {
            GamePlayer player => [player],
            Group group => group.GetPlayersInTheGroup(),
            _ => [],
        };

        /// <summary>Quest item id (lower case) to the ids of the data quests that name it anywhere in their row or in
        /// their classic-quests.json entry. Built once.</summary>
        private static Dictionary<string, HashSet<int>> QuestsByItem()
        {
            if (_questsByItem != null)
                return _questsByItem;
            lock (Sync)
            {
                if (_questsByItem != null)
                    return _questsByItem;
                var map = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
                PropertyInfo[] text = typeof(DbDataQuest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.PropertyType == typeof(string) && p.CanRead).ToArray();
                foreach (DbDataQuest row in GameServer.Database.SelectAllObjects<DbDataQuest>())
                    foreach (PropertyInfo p in text)
                        Add(map, row.ID, p.GetValue(row) as string);
                foreach ((int id, string entry) in ClassicQuests.QuestTexts())
                    Add(map, id, entry);
                return _questsByItem = map;
            }
        }

        /// <summary>Tests only: the item map is read again on next use.</summary>
        internal static void Reset() => _questsByItem = null;

        private static void Add(Dictionary<string, HashSet<int>> map, int questId, string text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("cq_", StringComparison.OrdinalIgnoreCase))
                return;
            foreach (Match match in QuestItemId.Matches(text))
            {
                string id = match.Value.ToLowerInvariant();
                if (!map.TryGetValue(id, out HashSet<int> quests))
                    map[id] = quests = new HashSet<int>();
                quests.Add(questId);
            }
        }
    }
}
