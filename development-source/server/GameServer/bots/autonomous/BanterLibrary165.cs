using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DOL.GS
{
    /// <summary>
    /// Data-driven bot banter (owner 2026-11-10: "1000's of new lines", relevant to raids, rallies, RvR, battlegrounds
    /// and replies to players). Lines live in banter165/*.json next to the server; this class holds only pure rules:
    /// parse and check a file, pick a line for a situation, fill its placeholders, map chat to a reply situation.
    /// Live wiring (what a bot is doing, rate limits, channels) is in BotBanter165. Keep the vocabulary below in step
    /// with tools/validate_banter165.py.
    /// </summary>
    public static class BanterLibrary165
    {
        public const string FolderName = "banter165";
        public const int MinLength = 4;
        public const int MaxLength = 160;

        public static readonly string[] Channels = ["realm", "guild", "say", "group"];
        public static readonly string[] Realms = ["Albion", "Midgard", "Hibernia"];
        public static readonly string[] ClassTypes = ["tank", "melee", "healer", "support", "caster", "pet", "stealth", "archer"];
        public static readonly string[] Kinds = ["open", "reply"];

        /// <summary>
        /// Placeholder values a line's "only" may test: the line is said only when the speaker's value is one of the
        /// names listed, so a line about one place, monster, boss or class is never said about another.
        /// </summary>
        public static readonly string[] OnlyKeys = ["zone", "class", "target", "boss", "raid", "keep", "bg", "destination", "time", "thread"];

        /// <summary>"only": { "time": ... } names: the game clock where the speaker stands (night is 18:00 to 06:00).</summary>
        public static readonly string[] Times = ["day", "night"];

        /// <summary>How often a reply to an opener that "invites" something is taken from the lines written for that thread.</summary>
        public const double ThreadReplyChance = 0.75;

        /// <summary>Open situations whose zone lines also answer a player who asks about that zone.</summary>
        public static readonly string[] PlaceTalk = ["dungeon", "hunting", "travel", "idle"];

        /// <summary>Placeholders every situation can use (the speaker's own facts).</summary>
        public static readonly string[] AlwaysPlaceholders = ["bot", "realm", "enemyrealm", "level", "class", "zone"];

        private static readonly string[] ReplyExtra = ["guild", "player", "target", "destination"];
        private static readonly string[] BgExtra = ["guild", "bg"];
        private static readonly string[] KeepExtra = ["guild", "keep"];

        /// <summary>Every known situation key and the extra placeholders it may use besides AlwaysPlaceholders.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Situations = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // everyday
            ["idle"] = ["guild", "target", "destination"],
            ["town"] = ["guild", "item"],
            ["travel"] = ["guild", "destination"],
            ["hunting"] = ["guild", "target"],
            ["dungeon"] = ["guild", "target"],
            ["lfg"] = ["guild", "target"],
            ["group_hunt"] = ["guild", "target"],
            ["death"] = ["guild", "target"],
            ["resurrected"] = ["guild"],
            ["levelup"] = ["guild"],
            ["trading"] = ["guild", "item"],
            ["crafting"] = ["guild"],
            // great hunts (realm raids) and the Horn of Rallying
            ["raid_forming"] = ["guild", "raid"],
            ["raid_march"] = ["guild", "raid"],
            ["raid_boss"] = ["guild", "raid", "boss"],
            ["raid_victory"] = ["guild", "raid"],
            ["raid_ended"] = ["guild", "raid"],
            ["rally_horn"] = ["guild", "raid", "player"],
            // frontier
            ["rvr_patrol"] = ["guild"],
            ["rvr_siege"] = KeepExtra,
            ["rvr_defend"] = KeepExtra,
            ["rvr_relic"] = KeepExtra,
            ["rvr_victory"] = KeepExtra,
            ["rvr_defeat"] = KeepExtra,
            // battlegrounds
            ["battleground"] = BgExtra,
            ["bg_abermenai"] = BgExtra,
            ["bg_thidranki"] = BgExtra,
            ["bg_murdaigean"] = BgExtra,
            ["bg_caledonia"] = BgExtra,
            // guild and conversation glue
            ["guild_join"] = ["guild"],
            ["chat_close"] = ["guild"],
            // replies to a player
            ["reply_greeting"] = ReplyExtra,
            ["reply_farewell"] = ReplyExtra,
            ["reply_thanks"] = ReplyExtra,
            ["reply_ding"] = ReplyExtra,
            ["reply_insult"] = ReplyExtra,
            ["reply_banter"] = ReplyExtra,
            ["reply_stop"] = ReplyExtra,
            ["reply_named"] = ReplyExtra,
            ["reply_offtopic"] = ReplyExtra,
            ["reply_lfg"] = ReplyExtra,
            ["reply_help"] = ReplyExtra,
            ["reply_where"] = ReplyExtra,
            ["reply_what"] = ReplyExtra,
            ["reply_rvr"] = ReplyExtra,
            ["reply_raid"] = ReplyExtra,
            ["reply_bg"] = ReplyExtra,
            ["reply_trade"] = ReplyExtra,
            ["reply_dungeon"] = ReplyExtra,
            ["reply_general"] = ReplyExtra,
        };

        public static readonly HashSet<string> AllPlaceholders = new(
            AlwaysPlaceholders.Concat(Situations.Values.SelectMany(extra => extra)), StringComparer.Ordinal);

        public static bool IsReplySituation(string situation) => situation?.StartsWith("reply_", StringComparison.Ordinal) == true;

        /// <summary>Situations whose lines have no open/reply kind (said once, by one speaker).</summary>
        public static bool IsKindless(string situation) => IsReplySituation(situation) || situation is "chat_close" or "guild_join";

        /// <summary>Words a line may never contain: meta talk, insults, later expansions (patch 1.65 only), real world.</summary>
        public static readonly string[] ForbiddenWords =
        [
            "bot", "bots", "gamebot", "gamebots", "npc", "npcs", "ai", "server", "script", "claude", "opendaoc", "emulator", "offline",
            "noob", "noobs", "newb", "idiot", "idiots", "moron", "morons", "stfu", "retard", "retarded", "shit", "fuck", "fucking",
            "bitch", "bastard", "ass", "fag", "atlantis", "toa", "catacombs", "cata", "labyrinth", "agramon", "molvik", "leirvik",
            "wilton", "killaloe", "cathal", "mauler", "maulers", "vampiir", "bainshee", "heretic", "heretics", "warlock", "warlocks",
            "valkyrie", "valkyries", "oceanus", "stygia", "volcanus", "aerus", "artifact", "artifacts", "ml", "cl", "mythirian",
            "politics", "president", "election", "bitcoin", "crypto", "football", "netflix", "internet", "facebook", "reddit",
            "discord", "twitch", "youtube", "phone",
        ];

        public static readonly string[] ForbiddenPhrases =
        [
            "champion level", "master level", "new frontiers", "darkness rising", "trials of", "/go", "real life", "irl",
        ];

        private static readonly HashSet<string> ForbiddenWordSet = new(ForbiddenWords, StringComparer.OrdinalIgnoreCase);
        private static readonly Regex PlaceholderRegex = new(@"\{([a-z]+)\}", RegexOptions.Compiled);
        private static readonly Regex WordRegex = new(@"[A-Za-z']+", RegexOptions.Compiled);
        private static readonly Regex ThreadRegex = new(@"^[a-z]+(_[a-z]+)*$", RegexOptions.Compiled);

        /// <summary>One loaded line. Masks keep a pick cheap at 1,500+ bots.</summary>
        public sealed class Line
        {
            public int Id;
            public string Text;
            public string[] Situations;
            public int ChannelMask;     // 0 = any channel
            public int RealmMask;       // 0 = any realm (bit 1 Albion, 2 Midgard, 4 Hibernia)
            public int MinLevel = 1;
            public int MaxLevel = 50;
            public int ClassMask;       // 0 = any class type
            public bool Reply;          // kind "reply"
            public double Weight = 1;
            public string[] Placeholders;
            /// <summary>"only": placeholder name to the values it must be one of; null when the line has no such test.</summary>
            public Dictionary<string, string[]> Only;
            /// <summary>"invites": what this opening line asks for ("boast", "joke"); replies that test only.thread answer it.</summary>
            public string Invites;
            public string File;
        }

        /// <summary>A loaded set of lines indexed by situation.</summary>
        public sealed class BanterSet
        {
            public readonly Dictionary<string, List<Line>> BySituation = new(StringComparer.Ordinal);
            public readonly List<string> Errors = new();
            public int Count;
            public int Files;

            public static readonly BanterSet Empty = new();

            public int CountFor(string situation) => BySituation.TryGetValue(situation, out List<Line> lines) ? lines.Count : 0;
        }

        /// <summary>What a pick asks for. Values holds the placeholder values this call can fill.</summary>
        public sealed record Query(IReadOnlyList<string> Situations, string Channel, eRealm Realm, int Level, string ClassName,
            bool? Reply, IReadOnlyDictionary<string, string> Values, string MustTest = null);

        #region Loading

        /// <summary>Reads every *.json in a folder (files starting with '_' are skipped). Bad lines are skipped and listed in Errors.</summary>
        public static BanterSet LoadFolder(string folder)
        {
            var set = new BanterSet();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return set;
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(folder, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(path);
                if (name.StartsWith("_", StringComparison.Ordinal))
                    continue;
                string json;
                try { json = File.ReadAllText(path, Encoding.UTF8); }
                catch (Exception exception) { set.Errors.Add($"{name}: cannot read ({exception.Message})"); continue; }
                AddFile(set, name, json, seen);
            }
            return set;
        }

        /// <summary>Parses one file into the set. seen maps normalized text to "file" for the duplicate check.</summary>
        public static void AddFile(BanterSet set, string fileName, string json, Dictionary<string, string> seen = null)
        {
            seen ??= new Dictionary<string, string>(StringComparer.Ordinal);
            string stem = Path.GetFileNameWithoutExtension(fileName);
            if (!Regex.IsMatch(fileName, "^[a-z0-9_]+\\.json$"))
                set.Errors.Add($"{fileName}: file name must be lower-case letters, digits and _ ending in .json");
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            }
            catch (JsonException exception)
            {
                set.Errors.Add($"{fileName}: invalid JSON ({exception.Message})");
                return;
            }
            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    set.Errors.Add($"{fileName}: the top level must be an object");
                    return;
                }
                foreach (JsonProperty property in root.EnumerateObject())
                    if (property.Name is not ("category" or "description" or "defaults" or "lines"))
                        set.Errors.Add($"{fileName}: unknown top-level key \"{property.Name}\"");
                if (!root.TryGetProperty("category", out JsonElement category) || category.ValueKind != JsonValueKind.String || category.GetString() != stem)
                    set.Errors.Add($"{fileName}: \"category\" must equal the file name \"{stem}\"");
                JsonElement defaults = root.TryGetProperty("defaults", out JsonElement d) ? d : default;
                if (defaults.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object))
                {
                    set.Errors.Add($"{fileName}: \"defaults\" must be an object");
                    defaults = default;
                }
                if (defaults.ValueKind == JsonValueKind.Object && defaults.TryGetProperty("text", out _))
                    set.Errors.Add($"{fileName}: \"defaults\" may not set \"text\"");
                if (!root.TryGetProperty("lines", out JsonElement lines) || lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() == 0)
                {
                    set.Errors.Add($"{fileName}: \"lines\" must be a non-empty array");
                    return;
                }
                set.Files++;
                int index = 0;
                foreach (JsonElement element in lines.EnumerateArray())
                {
                    index++;
                    string where = $"{fileName} line {index}";
                    var errors = new List<string>();
                    Line line = ParseLine(element, defaults, errors);
                    if (line != null)
                    {
                        string key = Normalize(line.Text);
                        if (seen.TryGetValue(key, out string other))
                            errors.Add($"duplicate of a line in {other}");
                        else
                            seen[key] = fileName;
                    }
                    if (errors.Count > 0 || line == null)
                    {
                        foreach (string error in errors)
                            set.Errors.Add($"{where}: {error}");
                        continue;
                    }
                    line.Id = set.Count++;
                    line.File = stem;
                    foreach (string situation in line.Situations)
                    {
                        if (!set.BySituation.TryGetValue(situation, out List<Line> list))
                            set.BySituation[situation] = list = new List<Line>();
                        list.Add(line);
                    }
                }
            }
        }

        private static JsonElement Field(JsonElement line, JsonElement defaults, string name)
        {
            if (line.TryGetProperty(name, out JsonElement own))
                return own;
            return defaults.ValueKind == JsonValueKind.Object && defaults.TryGetProperty(name, out JsonElement fallback) ? fallback : default;
        }

        private static readonly string[] LineKeys = ["text", "situation", "channel", "realm", "level", "classType", "kind", "weight", "only", "invites"];

        /// <summary>One line object (with file defaults applied); null when it cannot be used. Problems go to errors.</summary>
        public static Line ParseLine(JsonElement element, JsonElement defaults, List<string> errors)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                errors.Add("each line must be an object");
                return null;
            }
            foreach (JsonProperty property in element.EnumerateObject())
                if (Array.IndexOf(LineKeys, property.Name) < 0)
                    errors.Add($"unknown key \"{property.Name}\"");
            if (defaults.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty property in defaults.EnumerateObject())
                    if (Array.IndexOf(LineKeys, property.Name) < 0)
                        errors.Add($"unknown key \"{property.Name}\" in defaults");

            JsonElement text = element.TryGetProperty("text", out JsonElement t) ? t : default;
            if (text.ValueKind != JsonValueKind.String)
            {
                errors.Add("\"text\" must be a string");
                return null;
            }
            var line = new Line { Text = text.GetString() };

            string[] situations = Strings(Field(element, defaults, "situation"), "situation", errors, single: true);
            if (situations == null || situations.Length == 0)
                errors.Add("\"situation\" is required");
            else
            {
                foreach (string situation in situations)
                    if (!Situations.ContainsKey(situation))
                        errors.Add($"unknown situation \"{situation}\"");
                if (situations.Any(IsReplySituation) && !situations.All(IsReplySituation))
                    errors.Add("reply_* situations cannot be mixed with other situations on one line");
                if (situations.Distinct().Count() != situations.Length)
                    errors.Add("a situation is listed twice");
            }
            line.Situations = situations ?? Array.Empty<string>();

            string[] channels = Strings(Field(element, defaults, "channel"), "channel", errors, single: true);
            if (channels != null)
                foreach (string channel in channels)
                {
                    int bit = Array.IndexOf(Channels, channel);
                    if (bit < 0) errors.Add($"unknown channel \"{channel}\"");
                    else line.ChannelMask |= 1 << bit;
                }
            string[] realms = Strings(Field(element, defaults, "realm"), "realm", errors, single: true);
            if (realms != null)
                foreach (string realm in realms)
                {
                    int bit = Array.IndexOf(Realms, realm);
                    if (bit < 0) errors.Add($"unknown realm \"{realm}\" (Albion, Midgard or Hibernia)");
                    else line.RealmMask |= 1 << bit;
                }
            string[] types = Strings(Field(element, defaults, "classType"), "classType", errors, single: true);
            if (types != null)
                foreach (string type in types)
                {
                    int bit = Array.IndexOf(ClassTypes, type);
                    if (bit < 0) errors.Add($"unknown classType \"{type}\"");
                    else line.ClassMask |= 1 << bit;
                }

            JsonElement level = Field(element, defaults, "level");
            if (level.ValueKind != JsonValueKind.Undefined)
            {
                if (level.ValueKind != JsonValueKind.Array || level.GetArrayLength() != 2 ||
                    !level[0].TryGetInt32(out int min) || !level[1].TryGetInt32(out int max) || min < 1 || max > 50 || min > max)
                    errors.Add("\"level\" must be [min, max] with 1 <= min <= max <= 50");
                else
                {
                    line.MinLevel = min;
                    line.MaxLevel = max;
                }
            }

            JsonElement kind = Field(element, defaults, "kind");
            if (kind.ValueKind != JsonValueKind.Undefined)
            {
                string value = kind.ValueKind == JsonValueKind.String ? kind.GetString() : null;
                if (Array.IndexOf(Kinds, value) < 0)
                    errors.Add("\"kind\" must be \"open\" or \"reply\"");
                else if (line.Situations.Any(IsKindless) && element.TryGetProperty("kind", out _))
                    errors.Add("\"kind\" is not used with reply_*, chat_close or guild_join lines; leave it out");
                else
                    line.Reply = value == "reply" && !line.Situations.Any(IsKindless);
            }
            if (line.Situations.Any(IsReplySituation))
                line.Reply = true;

            JsonElement weight = Field(element, defaults, "weight");
            if (weight.ValueKind != JsonValueKind.Undefined)
            {
                if (weight.ValueKind != JsonValueKind.Number || !weight.TryGetDouble(out double w) || w <= 0 || w > 10)
                    errors.Add("\"weight\" must be a number above 0 and at most 10");
                else
                    line.Weight = w;
            }

            // channel rules: guild_join is guild only; the group channel is used only for replies
            if (line.Situations.Contains("guild_join") && line.ChannelMask != 0 && line.ChannelMask != 1 << 1)
                errors.Add("guild_join lines are guild channel only");
            if (line.ChannelMask == 1 << 3 && !line.Situations.All(IsReplySituation))
                errors.Add("the group channel is only used for reply_* lines");

            line.Only = ParseOnly(Field(element, defaults, "only"), line.Situations, line.Reply, errors);

            JsonElement invites = Field(element, defaults, "invites");
            if (invites.ValueKind != JsonValueKind.Undefined)
            {
                string word = invites.ValueKind == JsonValueKind.String ? invites.GetString() : null;
                if (word == null || !ThreadRegex.IsMatch(word))
                    errors.Add("\"invites\" must be one word of lower-case letters, such as \"boast\"");
                else if (line.Reply || line.Situations.Any(IsKindless))
                    errors.Add("\"invites\" is for an opening line (not kind reply, reply_*, chat_close or guild_join)");
                else
                    line.Invites = word;
            }

            errors.AddRange(CheckText(line.Text, line.Situations, out line.Placeholders));
            return line;
        }

        /// <summary>
        /// "only": { "zone": ["Spindelhalla"], "class": "Skald" }. Each key is a placeholder from OnlyKeys that the
        /// line's situations can fill; each value is one name or a list of names (compared without case).
        /// </summary>
        private static Dictionary<string, string[]> ParseOnly(JsonElement only, IReadOnlyCollection<string> situations, bool reply, List<string> errors)
        {
            if (only.ValueKind == JsonValueKind.Undefined)
                return null;
            if (only.ValueKind != JsonValueKind.Object || !only.EnumerateObject().Any())
            {
                errors.Add("\"only\" must be an object such as { \"zone\": [\"Spindelhalla\"] }");
                return null;
            }
            var tests = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (JsonProperty property in only.EnumerateObject())
            {
                if (Array.IndexOf(OnlyKeys, property.Name) < 0)
                {
                    errors.Add($"\"only\" cannot test \"{property.Name}\" (it tests {string.Join(", ", OnlyKeys)})");
                    continue;
                }
                string[] names = Strings(property.Value, "only." + property.Name, errors, single: true);
                if (names == null)
                    continue;
                if (names.Any(name => string.IsNullOrWhiteSpace(name) || name != name.Trim()))
                {
                    errors.Add($"\"only.{property.Name}\" has an empty name or one with spaces around it");
                    continue;
                }
                if (property.Name == "time")
                {
                    if (names.Any(name => Array.IndexOf(Times, name) < 0))
                        errors.Add("\"only.time\" is \"day\" or \"night\"");
                }
                else if (property.Name == "thread")
                {
                    if (!reply || situations.Any(IsKindless))
                        errors.Add("\"only\" tests thread, which only a kind reply line can follow");
                    else if (names.Any(name => !ThreadRegex.IsMatch(name)))
                        errors.Add("\"only.thread\" names must be one word of lower-case letters, such as \"boast\"");
                }
                else if (Array.IndexOf(AlwaysPlaceholders, property.Name) < 0)
                    foreach (string situation in situations)
                        if (Situations.TryGetValue(situation, out string[] extra) && Array.IndexOf(extra, property.Name) < 0)
                            errors.Add($"\"only\" tests {{{property.Name}}}, which is never filled in situation {situation}");
                tests[property.Name] = names;
            }
            return tests.Count > 0 ? tests : null;
        }

        private static string[] Strings(JsonElement value, string name, List<string> errors, bool single)
        {
            if (value.ValueKind == JsonValueKind.Undefined)
                return null;
            if (single && value.ValueKind == JsonValueKind.String)
                return [value.GetString()];
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0 || value.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String))
            {
                errors.Add($"\"{name}\" must be a string or a non-empty array of strings");
                return null;
            }
            return value.EnumerateArray().Select(v => v.GetString()).ToArray();
        }

        /// <summary>Text rules writers must follow. Returns problems; placeholders gets the tokens used.</summary>
        public static List<string> CheckText(string text, IReadOnlyCollection<string> situations, out string[] placeholders)
        {
            var errors = new List<string>();
            placeholders = [];
            if (text == null)
            {
                errors.Add("text is missing");
                return errors;
            }
            if (text.Length < MinLength || text.Length > MaxLength)
                errors.Add($"text must be {MinLength}-{MaxLength} characters (has {text.Length})");
            if (text != text.Trim())
                errors.Add("text has leading or trailing spaces");
            if (text.Contains("  ", StringComparison.Ordinal))
                errors.Add("text has a double space");
            if (text.Any(c => c < 0x20 || c > 0x7E))
                errors.Add("text must be plain printable ASCII (no curly quotes, dashes, accents or tabs)");
            if (text.Contains('"'))
                errors.Add("text may not contain double quotes");

            placeholders = PlaceholderRegex.Matches(text).Select(m => m.Groups[1].Value).Distinct().ToArray();
            string stripped = PlaceholderRegex.Replace(text, string.Empty);
            if (stripped.Contains('{') || stripped.Contains('}'))
                errors.Add("text has a stray { or } (placeholders are lower-case, like {zone})");
            foreach (string token in placeholders)
            {
                if (!AllPlaceholders.Contains(token))
                {
                    errors.Add($"unknown placeholder {{{token}}}");
                    continue;
                }
                if (Array.IndexOf(AlwaysPlaceholders, token) >= 0 || situations == null)
                    continue;
                foreach (string situation in situations)
                    if (Situations.TryGetValue(situation, out string[] extra) && Array.IndexOf(extra, token) < 0)
                        errors.Add($"placeholder {{{token}}} is never filled in situation {situation}");
            }

            string words = PlaceholderRegex.Replace(text, " ");
            foreach (Match word in WordRegex.Matches(words))
                if (ForbiddenWordSet.Contains(word.Value.Trim('\'')))
                    errors.Add($"forbidden word \"{word.Value}\"");
            foreach (string phrase in ForbiddenPhrases)
                if (Regex.IsMatch(words, $@"(^|[^a-z]){Regex.Escape(phrase)}s?($|[^a-z])", RegexOptions.IgnoreCase))
                    errors.Add($"forbidden phrase \"{phrase}\"");
            return errors;
        }

        /// <summary>Duplicate key: lower case, letters/digits/placeholders only, single spaces.</summary>
        public static string Normalize(string text)
        {
            var builder = new StringBuilder(text?.Length ?? 0);
            bool space = false;
            foreach (char c in (text ?? string.Empty).ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c is '{' or '}')
                {
                    if (space && builder.Length > 0) builder.Append(' ');
                    builder.Append(c);
                    space = false;
                }
                else
                    space = true;
            }
            return builder.ToString();
        }

        #endregion

        #region Picking

        public static int RealmBit(eRealm realm) => realm switch
        {
            eRealm.Albion => 1,
            eRealm.Midgard => 2,
            eRealm.Hibernia => 4,
            _ => 0,
        };

        public static int ChannelBit(string channel)
        {
            int index = Array.IndexOf(Channels, channel);
            return index < 0 ? 0 : 1 << index;
        }

        /// <summary>Does a line suit this query (channel, realm, level, class type, kind, fillable placeholders, "only")?</summary>
        public static bool Fits(Line line, string situation, Query query, int classMask)
        {
            if (line.ChannelMask != 0 && (line.ChannelMask & ChannelBit(query.Channel)) == 0)
                return false;
            int realm = RealmBit(query.Realm);
            if (line.RealmMask != 0 && (line.RealmMask & realm) == 0)
                return false;
            if (query.Level < line.MinLevel || query.Level > line.MaxLevel)
                return false;
            if (line.ClassMask != 0 && (line.ClassMask & classMask) == 0)
                return false;
            if (query.Reply.HasValue && !IsKindless(situation) && line.Reply != query.Reply.Value)
                return false;
            foreach (string token in line.Placeholders)
                if (query.Values == null || !query.Values.TryGetValue(token, out string value) || string.IsNullOrWhiteSpace(value))
                    return false;
            if (query.MustTest != null && (line.Only == null || !line.Only.ContainsKey(query.MustTest)))
                return false;
            if (line.Only != null)
                foreach (KeyValuePair<string, string[]> test in line.Only)
                {
                    if (query.Values == null || !query.Values.TryGetValue(test.Key, out string have) || string.IsNullOrWhiteSpace(have))
                        return false;
                    have = have.Trim();
                    bool named = false;
                    foreach (string name in test.Value)
                        if (string.Equals(name, have, StringComparison.OrdinalIgnoreCase))
                        {
                            named = true;
                            break;
                        }
                    if (!named)
                        return false;
                }
            return true;
        }

        /// <summary>How many lines could answer the query's first situation that has any.</summary>
        public static int CountCandidates(BanterSet set, Query query, out string situation)
        {
            situation = null;
            if (set == null || query?.Situations == null)
                return 0;
            int classMask = ClassMask(query.ClassName);
            foreach (string key in query.Situations)
            {
                if (key == null || !set.BySituation.TryGetValue(key, out List<Line> lines))
                    continue;
                int count = 0;
                foreach (Line line in lines)
                    if (Fits(line, key, query, classMask))
                        count++;
                if (count > 0)
                {
                    situation = key;
                    return count;
                }
            }
            return 0;
        }

        /// <summary>
        /// Weighted pick from the first situation in the query that has a fitting line; lines used recently are
        /// skipped while others remain. Null when nothing fits (the caller falls back to today's lines).
        /// </summary>
        public static Line Choose(BanterSet set, Query query, Random random, Func<int, bool> recentlyUsed, out string situation)
        {
            situation = null;
            if (set == null || query?.Situations == null)
                return null;
            random ??= Random.Shared;
            int classMask = ClassMask(query.ClassName);
            var fresh = new List<Line>();
            var stale = new List<Line>();
            foreach (string key in query.Situations)
            {
                if (key == null || !set.BySituation.TryGetValue(key, out List<Line> lines))
                    continue;
                foreach (Line line in lines)
                {
                    if (!Fits(line, key, query, classMask))
                        continue;
                    (recentlyUsed != null && recentlyUsed(line.Id) ? stale : fresh).Add(line);
                }
                if (fresh.Count > 0 || stale.Count > 0)
                {
                    situation = key;
                    return Weighted(fresh.Count > 0 ? fresh : stale, random);
                }
            }
            return null;
        }

        private static Line Weighted(List<Line> lines, Random random)
        {
            double total = 0;
            foreach (Line line in lines)
                total += line.Weight;
            double roll = random.NextDouble() * total;
            foreach (Line line in lines)
            {
                roll -= line.Weight;
                if (roll < 0)
                    return line;
            }
            return lines[^1];
        }

        /// <summary>Fills {tokens}; an unknown or empty token is left out cleanly.</summary>
        public static string Fill(string text, IReadOnlyDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            string filled = PlaceholderRegex.Replace(text, match =>
                values != null && values.TryGetValue(match.Groups[1].Value, out string value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : string.Empty);
            return Regex.Replace(filled, " {2,}", " ").Trim();
        }

        /// <summary>
        /// How often a library line is used instead of today's built-in line: never without candidates, about half
        /// the time with a few, 85% with a full set. Keeps the old lines in the mix (owner 2026-11-10).
        /// </summary>
        public static double UseLibraryChance(int candidates) =>
            candidates <= 0 ? 0 : Math.Min(0.85, 0.5 + candidates * 0.0175);

        private static readonly HashSet<string> BuiltIn = new(StringComparer.Ordinal)
        {
            "idle", "town", "travel", "hunting", "dungeon", "lfg", "group_hunt", "trading", "chat_close", "guild_join",
            "reply_greeting", "reply_farewell", "reply_insult", "reply_banter", "reply_stop", "reply_named", "reply_offtopic",
            "reply_lfg", "reply_help", "reply_where", "reply_what", "reply_rvr", "reply_trade", "reply_dungeon", "reply_general",
        };

        /// <summary>Situations today's code already has lines for (those share the stage); others always use the library.</summary>
        public static bool BuiltInCovers(string situation) => situation != null && BuiltIn.Contains(situation);

        #endregion

        #region Class types

        private static readonly Dictionary<string, string[]> ClassTypeTable = new(StringComparer.OrdinalIgnoreCase)
        {
            // Albion
            ["Armsman"] = ["tank", "melee"], ["Paladin"] = ["tank", "melee", "support"], ["Mercenary"] = ["melee"],
            ["Reaver"] = ["melee", "tank"], ["Cleric"] = ["healer"], ["Friar"] = ["healer", "melee", "support"],
            ["Wizard"] = ["caster"], ["Sorcerer"] = ["caster", "pet"], ["Theurgist"] = ["caster", "pet"],
            ["Cabalist"] = ["caster", "pet"], ["Necromancer"] = ["pet", "caster"], ["Minstrel"] = ["support", "stealth"],
            ["Infiltrator"] = ["stealth", "melee"], ["Scout"] = ["archer", "stealth"],
            ["Fighter"] = ["melee"], ["Acolyte"] = ["healer"], ["Elementalist"] = ["caster"], ["Mage"] = ["caster"],
            ["Rogue"] = ["melee"], ["Disciple"] = ["caster"],
            // Midgard
            ["Warrior"] = ["tank", "melee"], ["Thane"] = ["tank", "melee", "caster"], ["Berserker"] = ["melee"],
            ["Savage"] = ["melee"], ["Skald"] = ["support", "melee"], ["Healer"] = ["healer"], ["Shaman"] = ["healer", "support"],
            ["Runemaster"] = ["caster"], ["Spiritmaster"] = ["caster", "pet"], ["Bonedancer"] = ["pet", "caster"],
            ["Shadowblade"] = ["stealth", "melee"], ["Hunter"] = ["archer", "stealth", "pet"],
            ["Viking"] = ["melee"], ["Seer"] = ["healer"], ["Mystic"] = ["caster"], ["MidgardRogue"] = ["melee"],
            // Hibernia
            ["Hero"] = ["tank", "melee"], ["Blademaster"] = ["melee"], ["Champion"] = ["melee", "tank"],
            ["Warden"] = ["support", "melee"], ["Druid"] = ["healer", "pet"], ["Bard"] = ["support", "healer"],
            ["Eldritch"] = ["caster"], ["Enchanter"] = ["caster", "pet"], ["Mentalist"] = ["caster", "healer"],
            ["Nightshade"] = ["stealth", "melee"], ["Ranger"] = ["archer", "stealth"], ["Animist"] = ["pet", "caster"],
            ["Valewalker"] = ["melee"], ["Guardian"] = ["melee"], ["Naturalist"] = ["healer"], ["Magician"] = ["caster"],
            ["Stalker"] = ["melee"], ["Forester"] = ["caster"],
        };

        public static string[] ClassTypesOf(string className) =>
            className != null && ClassTypeTable.TryGetValue(className.Trim(), out string[] types) ? types : [];

        public static int ClassMask(string className)
        {
            int mask = 0;
            foreach (string type in ClassTypesOf(className))
                mask |= 1 << Array.IndexOf(ClassTypes, type);
            return mask;
        }

        #endregion

        #region Situations

        /// <summary>What a bot is doing when it starts talking (filled by BotBanter165 from live state; pure here).</summary>
        public sealed record Facts(
            string Battleground = null,   // "thidranki" etc. while inside one
            string RaidState = null,      // "forming", "march" or "boss"
            string RvrRole = null,        // "siege", "defend" or "relic"
            bool Frontier = false,
            bool Dungeon = false,
            bool Town = false,
            bool Crafting = false,
            bool Trading = false,
            bool Traveling = false,
            bool HasTarget = false,
            int GroupSize = 1,
            bool LeveledUp = false,
            bool RecentDeath = false,
            bool ResurrectedInPlace = false);

        /// <summary>
        /// The situations to try for an opening line, most specific first; "idle" is always last so there is
        /// something to say. Events beat places, places beat the everyday.
        /// </summary>
        public static List<string> ResolveAmbient(Facts facts, Random random)
        {
            random ??= Random.Shared;
            var keys = new List<string>(4);
            if (facts == null)
            {
                keys.Add("idle");
                return keys;
            }
            // LeveledUp is true only shortly after the level came (IsRecentLevelUp); half the time it leads (owner 2026-11-10)
            if (facts.LeveledUp && random.NextDouble() < LevelUpChance)
                keys.Add("levelup");
            if (facts.RecentDeath && random.NextDouble() < 0.7)
                keys.Add(facts.ResurrectedInPlace ? "resurrected" : "death");
            if (facts.RaidState != null)
                keys.Add(facts.RaidState switch { "boss" => "raid_boss", "march" => "raid_march", _ => "raid_forming" });
            if (facts.RvrRole != null)
                keys.Add(facts.RvrRole switch { "defend" => "rvr_defend", "relic" => "rvr_relic", _ => "rvr_siege" });
            if (facts.Battleground != null)
            {
                keys.Add("bg_" + facts.Battleground.ToLowerInvariant());
                keys.Add("battleground");
            }
            else if (facts.Frontier && facts.RvrRole == null)
                keys.Add("rvr_patrol");
            if (facts.Dungeon)
                keys.Add("dungeon");
            if (facts.Crafting)
                keys.Add("crafting");
            if (facts.Trading && random.NextDouble() < 0.5)
                keys.Add("trading");
            if (facts.Town)
                keys.Add("town");
            if (facts.Traveling)
                keys.Add("travel");
            if (facts.GroupSize > 1)
                keys.Add("group_hunt");
            else if (!facts.Town && (facts.HasTarget ? random.NextDouble() < 0.3 : random.NextDouble() < 0.25))
                keys.Add("lfg");
            if (facts.HasTarget)
                keys.Add("hunting");
            keys.Add("idle");
            return keys;
        }

        /// <summary>How long after a level-up a bot may still talk about it, and how often it then does (owner 2026-11-10).</summary>
        public const long LevelUpWindowMilliseconds = 10 * 60_000;
        public const double LevelUpChance = 0.5;

        /// <summary>A level came within the window (levelUpTick 0 = no level-up seen since the server started).</summary>
        public static bool IsRecentLevelUp(long levelUpTick, long now) =>
            levelUpTick > 0 && now >= levelUpTick && now - levelUpTick < LevelUpWindowMilliseconds;

        /// <summary>Raised where it fell (close to its death spot) rather than released to its bind point.</summary>
        public static bool WasResurrectedInPlace(ushort deathRegion, int deathX, int deathY, ushort region, int x, int y, int radius = 2000) =>
            deathRegion != 0 && deathRegion == region && (long)(deathX - x) * (deathX - x) + (long)(deathY - y) * (deathY - y) <= (long)radius * radius;

        private static readonly Regex Thanks = new(@"\b(thanks|thank you|thank u|thx|ty|tyvm|cheers|appreciated)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Ding = new(@"\b(ding|dinged|leveled|levelled|level up|hit level)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Stop = new(@"\b(stop|quiet|shut up|be silent|enough)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Bg = new(@"\b(battleground|battlegrounds|bg|thid|thidranki|abermenai|murdaigean|caledonia)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RaidWords = new(@"\b(raid|raids|dragon|golestandt|gjalpinulva|cuuldurach|legion|horn|rally|rallying|epic|great hunt)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Where = new(@"\b(where|how do i get|how to get|location|located|which way)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Rez = new(@"\b(rez|res|rezz|resurrect|raise me)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Question = new(@"(\?\s*$)|^\s*(what|how|which|why|when|who|is|are|does|do|can|could|should|anyone know)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Period chat words the intent model does not know (thanks, ding, rez, battleground and raid words).</summary>
        public static bool HasPeriodChatWord(string message) =>
            !string.IsNullOrEmpty(message) && (Thanks.IsMatch(message) || Ding.IsMatch(message) || Rez.IsMatch(message) ||
                                               Bg.IsMatch(message) || RaidWords.IsMatch(message));

        /// <summary>
        /// An out-of-world reading the period words may undo ("ty", "rez pls"); a hard out-of-world word
        /// ("the epic football game") always wins (owner 2026-11-10), and so does one the intent model learned
        /// ("anyone see the epic movie"; review 2026-11-10, switch bot_chat_learned_offtopic_blocks_rescue).
        /// </summary>
        public static bool PeriodWordRescues(string message) =>
            HasPeriodChatWord(message) && !AutonomousChatIntentModel.HasOutOfWorldWord(message);

        /// <summary>
        /// The reply situations for a player's message, most fitting first, always ending in reply_general.
        /// Reuses the intent model; a few period chat words (ding, thx, rez, bg) are caught first.
        /// </summary>
        /// <summary>
        /// The place a message names, or null: the longest of the names that stands in it as whole words. Case,
        /// apostrophes and full stops do not count ("odins gate" names Odin's Gate). Pure.
        /// </summary>
        public static string PlaceNamed(string message, IEnumerable<string> names)
        {
            if (string.IsNullOrWhiteSpace(message) || names == null)
                return null;
            string said = " " + PlainWords(message) + " ";
            string best = null;
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || best != null && name.Length <= best.Length)
                    continue;
                string plain = PlainWords(name);
                if (plain.Length >= 4 && said.Contains(" " + plain + " ", StringComparison.Ordinal))
                    best = name;
            }
            return best;
        }

        private static string PlainWords(string text) =>
            Regex.Replace(Regex.Replace(text.ToLowerInvariant(), @"['.]", string.Empty), @"[^a-z0-9]+", " ").Trim();

        /// <summary>True when a message that names a place reads as a question about it (or was said to the bot).</summary>
        public static bool AsksAboutPlace(string message, bool directed)
        {
            // a message that names a zone is about the world, whatever the intent model makes of the zone's name
            string text = message ?? string.Empty;
            if (AutonomousChatIntentModel.Predict(text) == eAutonomousChatIntent.Abuse || directed && Stop.IsMatch(text))
                return false;
            return directed || Where.IsMatch(text) || Question.IsMatch(text);
        }

        public static List<string> ClassifyPlayerMessage(string message, bool directed)
        {
            var keys = new List<string>(4);
            string text = message ?? string.Empty;
            eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(text);
            // the intent model files unknown short words ("ding", "ty") under out-of-world; period chat words win over that
            // review 2026-11-10: an unsure reading whose only game word is a period word beside a real-life word
            // ("anyone see the epic movie") is off-topic too, not a raid reply
            if (intent == eAutonomousChatIntent.IgnoreOutOfWorld && !PeriodWordRescues(text) ||
                // owner 2026-11-10 (round 3): this unsure-reading branch is part of bot_chat_learned_offtopic_blocks_rescue
                AutonomousChatIntentModel.LearnedOffTopicBlocksRescue && intent == eAutonomousChatIntent.Unknown &&
                HasPeriodChatWord(text) && AutonomousChatIntentModel.HasOutOfWorldWord(text))
                keys.Add("reply_offtopic");
            else if (intent == eAutonomousChatIntent.Abuse)
                keys.Add("reply_insult");
            else if (directed && Stop.IsMatch(text))
                keys.Add("reply_stop");
            else
            {
                if (Thanks.IsMatch(text)) keys.Add("reply_thanks");
                if (Ding.IsMatch(text)) keys.Add("reply_ding");
                if (Rez.IsMatch(text)) keys.Add("reply_help");
                if (Bg.IsMatch(text)) keys.Add("reply_bg");
                if (RaidWords.IsMatch(text)) keys.Add("reply_raid");
                string byIntent = intent switch
                {
                    eAutonomousChatIntent.Greeting => "reply_greeting",
                    eAutonomousChatIntent.Farewell => "reply_farewell",
                    eAutonomousChatIntent.Banter => "reply_banter",
                    eAutonomousChatIntent.Grouping => "reply_lfg",
                    eAutonomousChatIntent.Grinding => "reply_lfg",
                    eAutonomousChatIntent.Help => "reply_help",
                    eAutonomousChatIntent.Travel => "reply_where",
                    eAutonomousChatIntent.RvR => "reply_rvr",
                    eAutonomousChatIntent.Trading => "reply_trade",
                    eAutonomousChatIntent.Dungeon => "reply_dungeon",
                    _ => null,
                };
                if (Where.IsMatch(text) && !keys.Contains("reply_where")) keys.Add("reply_where");
                if (byIntent != null && !keys.Contains(byIntent)) keys.Add(byIntent);
                if (Question.IsMatch(text)) keys.Add("reply_what");
            }
            if (directed) keys.Add("reply_named");
            keys.Add("reply_general");
            return keys.Distinct().ToList();
        }

        /// <summary>
        /// The situation of a realm herald notice (RealmEventNotices), read from its fixed wording, so bots can
        /// follow it with a line or two. Null when the notice is not one bots should chat about.
        /// </summary>
        public static string ClassifyNotice(string eventId, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            bool rvr = eventId?.StartsWith("rvr-", StringComparison.Ordinal) == true;
            bool Has(string phrase) => text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
            if (!rvr)
            {
                if (Has("withdraw") || Has("called off") || Has("could not finish") || Has("is spent") || Has("Not enough") ||
                    Has("without a confirmed") || Has("failed")) return "raid_ended";
                if (Has("Victory") || Has("final foe has fallen")) return "raid_victory";
                if (Has("advancing")) return "raid_boss";
                if (Has("departing together")) return "raid_march";
                return "raid_forming";
            }
            if (Has("is ours") || Has("We held") || Has("safe in our shrine") || Has("remain ours")) return "rvr_victory";
            if (Has("has fallen to") || Has("run out") || Has("could not break") || Has("Stand down") || Has("failed to gather") ||
                Has("standing down")) return "rvr_defeat";
            // a relic carried off by another realm is a loss, not a rally (owner 2026-11-10)
            if (Has("has secured") || Has("struggle on the road is over")) return "rvr_defeat";
            if (Has("struggle on the road has ended") || Has("fighting for") && Has("has ended")) return null;
            if (eventId.Contains("relic", StringComparison.Ordinal) || Has("relic")) return "rvr_relic";
            if (Has("under attack") || Has("Hold the walls") || Has("Stand ready") || Has("relieve") || Has("defense") || Has("defence"))
                return "rvr_defend";
            return "rvr_siege";
        }

        /// <summary>The keep id in a siege event id ("rvr-keep-42"), or -1.</summary>
        public static int KeepIdOf(string eventId)
        {
            const string prefix = "rvr-keep-";
            return eventId != null && eventId.StartsWith(prefix, StringComparison.Ordinal) &&
                   int.TryParse(eventId.AsSpan(prefix.Length), out int id) ? id : -1;
        }

        private static bool IsPlayableRealm(eRealm realm) => realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia;

        /// <summary>
        /// The real enemy of a bot of realm own in an event between attacker and defender: defenders face the attacker,
        /// attackers the defender, a third realm the keep's owner. None when neither is a real enemy (owner 2026-11-10).
        /// </summary>
        public static eRealm EnemyOf(eRealm own, eRealm attacker, eRealm defender)
        {
            eRealm enemy = own == defender ? attacker : own == attacker ? defender : IsPlayableRealm(defender) ? defender : attacker;
            return IsPlayableRealm(enemy) && enemy != own ? enemy : eRealm.None;
        }

        /// <summary>The one other realm a notice names ("Hibernia has secured ..."), or None when it names none or two.</summary>
        public static eRealm RealmNamedIn(string text, eRealm own)
        {
            if (string.IsNullOrEmpty(text))
                return eRealm.None;
            eRealm found = eRealm.None;
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                if (realm == own || !text.Contains(RealmName(realm), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (found != eRealm.None)
                    return eRealm.None;
                found = realm;
            }
            return found;
        }

        /// <summary>One of the two enemy realms (or the given one when known).</summary>
        public static string EnemyRealmName(eRealm own, eRealm known, Random random)
        {
            if (known is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia && known != own)
                return RealmName(known);
            eRealm[] others = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }.Where(r => r != own).ToArray();
            return RealmName(others[(random ?? Random.Shared).Next(others.Length)]);
        }

        public static string RealmName(eRealm realm) => realm switch
        {
            eRealm.Albion => "Albion",
            eRealm.Midgard => "Midgard",
            eRealm.Hibernia => "Hibernia",
            _ => string.Empty,
        };

        #endregion
    }
}
