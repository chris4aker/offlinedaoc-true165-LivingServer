using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// What an item's tooltip may show (owner 2026-11-05). Database notes in the Description column ("Non Public",
    /// "Classic level 50 class template", "(NULL)", the Atlas ROG salvage mark, our own builder notes) are never shown
    /// to players; real descriptions and "Classic quest reward (...)" lines still are. In True 1.65 Mode "Usable by"
    /// leaves out the classes that are not in 1.65 (the disabled_classes setting: Heretic, Valkyrie, Bainshee, Vampiir,
    /// Warlock, Maulers), e.g. Hammer of the Otherworld lists only the Reaver.
    /// </summary>
    public static class ItemTooltip165
    {
        public static bool ShowsDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return false;
            string text = description.Trim();
            return !(text.Equals("Non Public", StringComparison.OrdinalIgnoreCase) ||
                     text.Equals("(NULL)", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("Atlas ROG", StringComparison.Ordinal) ||
                     text.StartsWith("Classic level 50", StringComparison.OrdinalIgnoreCase) &&
                     text.Contains("class template", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("Loot gem (period", StringComparison.Ordinal));
        }

        private static string _disabledSource;
        private static HashSet<int> _disabled = [];

        private static HashSet<int> Disabled()
        {
            string source = ServerProperties.Properties.DISABLED_CLASSES ?? string.Empty;
            if (!ReferenceEquals(source, _disabledSource))
            {
                var set = new HashSet<int>();
                foreach (string entry in Util.SplitCSV(source, true))
                    if (int.TryParse(entry, out int id))
                        set.Add(id);
                _disabled = set;
                _disabledSource = source;
            }
            return _disabled;
        }

        /// <summary>
        /// The class ids to list under "Usable by". In True 1.65 Mode the disabled classes are left out, unless every
        /// listed class is disabled (then the list stays as it is, so the item never looks usable by everyone).
        /// </summary>
        public static List<int> UsableClasses(IEnumerable<int> allowed, bool classic165) =>
            UsableClasses(allowed, classic165 ? Disabled() : null);

        public static List<int> UsableClasses(IEnumerable<int> allowed, ISet<int> disabled)
        {
            List<int> all = allowed.ToList();
            if (disabled == null || disabled.Count == 0)
                return all;
            List<int> shown = all.Where(id => !disabled.Contains(id)).ToList();
            return shown.Count > 0 ? shown : all;
        }
    }
}
