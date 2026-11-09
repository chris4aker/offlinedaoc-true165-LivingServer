using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DOL.GS
{
    /// <summary>
    /// Bot name tables as stored in botnames165.json (lists are words separated by spaces; "-" = nothing).
    /// </summary>
    public sealed class BotNameTables
    {
        public sealed class GenderParts
        {
            public string Starts { get; set; } = string.Empty;
            public string Ends { get; set; } = string.Empty;
        }

        public sealed class RaceStyle
        {
            public string Realm { get; set; } = string.Empty;
            public bool Long { get; set; }
            public string Variants { get; set; } = string.Empty;
            public string Mids { get; set; } = "-";
            public GenderParts Male { get; set; } = new();
            public GenderParts Female { get; set; } = new();
        }

        public sealed class CompoundParts
        {
            public string First { get; set; } = string.Empty;
            public string Second { get; set; } = string.Empty;
        }

        public sealed class KindWeights
        {
            public int A { get; set; } = 50;
            public int B { get; set; } = 25;
            public int VariantA { get; set; } = 15;
            public int VariantB { get; set; } = 7;
            public int Compound { get; set; } = 3;
        }

        public int Version { get; set; } = 1;
        public string[] Help { get; set; } = [];
        public int MinLength { get; set; } = 4;
        public int MaxLength { get; set; } = 12;
        public KindWeights Weights { get; set; } = new();
        public string BotOnlyBlocked { get; set; } = string.Empty;
        public Dictionary<string, string> Bridges { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, CompoundParts> Compounds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RaceStyle> Races { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public static BotNameTables Parse(string json) => JsonSerializer.Deserialize<BotNameTables>(json, Options);
    }

    /// <summary>
    /// Fast "contains" blocklist (invalidnames.txt rules + the bot-only list). Patterns are matched case-insensitively
    /// against the lower-case name; "/regex/" lines are regular expressions, as in InvalidNamesManager.
    /// </summary>
    public sealed class BotNameBlocklist
    {
        private readonly Dictionary<int, string[]> _byPair;
        private readonly char[] _single;
        private readonly Regex[] _regex;

        public BotNameBlocklist(IEnumerable<string> lines)
        {
            var contains = new List<string>();
            var regex = new List<Regex>();
            foreach (string raw in lines ?? [])
            {
                if (string.IsNullOrEmpty(raw))
                    continue;
                string line = raw.TrimStart('﻿');
                if (line.Length == 0 || line[0] == '#')
                    continue;
                int comment = line.IndexOf('#');
                string pattern = comment >= 0 ? line[..comment].Trim() : line;
                if (pattern.Length == 0)
                    continue;
                if (pattern.Length > 1 && pattern.StartsWith('/') && pattern.EndsWith('/'))
                    regex.Add(new Regex(pattern.Trim('/'), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                else
                    contains.Add(pattern.ToLowerInvariant());
            }
            _single = contains.Where(p => p.Length == 1).Select(p => p[0]).Distinct().ToArray();
            _byPair = contains.Where(p => p.Length >= 2)
                .GroupBy(p => p[0] << 16 | p[1])
                .ToDictionary(g => g.Key, g => g.Distinct().ToArray());
            _regex = regex.ToArray();
        }

        public static BotNameBlocklist FromFile(string path, IEnumerable<string> extra)
        {
            var lines = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    lines.AddRange(File.ReadAllLines(path));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            lines.AddRange(extra ?? []);
            return new BotNameBlocklist(lines);
        }

        /// <summary>True when the (lower-case) name holds a blocked pattern.</summary>
        public bool IsBlocked(string lower)
        {
            if (_single.Length > 0 && lower.IndexOfAny(_single) >= 0)
                return true;
            for (int i = 0; i + 1 < lower.Length; i++)
            {
                if (!_byPair.TryGetValue(lower[i] << 16 | lower[i + 1], out string[] patterns))
                    continue;
                foreach (string pattern in patterns)
                    if (i + pattern.Length <= lower.Length && string.CompareOrdinal(lower, i, pattern, 0, pattern.Length) == 0)
                        return true;
            }
            foreach (Regex regex in _regex)
                if (regex.IsMatch(lower))
                    return true;
            return false;
        }
    }

    /// <summary>
    /// Bot name generator: 15 race styles, male and female tables, templates A (start+mid+end) and B (start+mid+bridge+end),
    /// player-style variants and realm compounds. Every pool is an index space; a Feistel permutation walks it so each
    /// name comes out once per counter. Pure code: no database (BotNameService does the claiming).
    /// </summary>
    public sealed class BotNameGenerator
    {
        public enum PoolKind : byte { A, B, VariantA, VariantB, Compound }

        /// <summary>One walkable index space (a race/gender template, its variants, or a realm's compounds).</summary>
        public sealed class SubPool
        {
            internal SubPool(string key, int poolId, eRealm realm, eRace race, eGender gender, PoolKind kind, long size, int weight, ulong signature)
            {
                Key = key; PoolId = poolId; Realm = realm; Race = race; Gender = gender; Kind = kind; Size = size; Weight = weight; Signature = signature;
            }

            /// <summary>Cursor key, e.g. "Briton/M/a" or "compound/Albion".</summary>
            public string Key { get; }
            /// <summary>Stable pool number stored with each claim (race*2+female, or 100+realm for compounds).</summary>
            public int PoolId { get; }
            public eRealm Realm { get; }
            public eRace Race { get; }
            public eGender Gender { get; }
            public PoolKind Kind { get; }
            public long Size { get; }
            public int Weight { get; }
            /// <summary>Hash of the parts; a table edit changes it and restarts the cursor.</summary>
            public ulong Signature { get; }

            internal string[] Starts, Mids, Bridges, Ends, Rules, First, Second;
        }

        /// <summary>The 15 Classic + SI player races (no Half Ogre, Frostalf, Shar or Minotaur).</summary>
        public static readonly IReadOnlyList<eRace> StyleRaces =
        [
            eRace.Briton, eRace.Avalonian, eRace.Highlander, eRace.Saracen, eRace.Inconnu,
            eRace.Norseman, eRace.Dwarf, eRace.Kobold, eRace.Troll, eRace.Valkyn,
            eRace.Celt, eRace.Lurikeen, eRace.Elf, eRace.Firbolg, eRace.Sylvan,
        ];

        public static eRealm RealmOf(eRace race) => race switch
        {
            eRace.Briton or eRace.Avalonian or eRace.Highlander or eRace.Saracen or eRace.Inconnu => eRealm.Albion,
            eRace.Norseman or eRace.Dwarf or eRace.Kobold or eRace.Troll or eRace.Valkyn => eRealm.Midgard,
            eRace.Celt or eRace.Lurikeen or eRace.Elf or eRace.Firbolg or eRace.Sylvan => eRealm.Hibernia,
            _ => eRealm.None,
        };

        /// <summary>"Albion", "Midgard" or "Hibernia" (eRealm.ToString() gives "_FirstPlayerRealm" for Albion).</summary>
        public static string RealmName(eRealm realm) => realm switch
        {
            eRealm.Albion => "Albion",
            eRealm.Midgard => "Midgard",
            eRealm.Hibernia => "Hibernia",
            _ => "None",
        };

        public static int PoolIdFor(eRace race, eGender gender) => (int)race * 2 + (gender == eGender.Female ? 1 : 0);
        public static int CompoundPoolId(eRealm realm) => 100 + (int)realm;

        private static readonly string[] KnownRules = ["double", "c2k", "i2y", "ix", "az", "son", "mac", "o"];

        private readonly Dictionary<(eRace, eGender), SubPool[]> _pools = new();
        private readonly Dictionary<eRealm, SubPool> _compounds = new();
        private readonly int _minLength, _maxLength;

        public BotNameTables Tables { get; }
        /// <summary>The invalid-names rules in use (the bot-only list is kept apart).</summary>
        public BotNameBlocklist Blocklist { get; }
        public IReadOnlyCollection<SubPool> AllPools => _pools.Values.SelectMany(p => p).Concat(_compounds.Values).ToArray();

        public BotNameGenerator(BotNameTables tables, BotNameBlocklist invalidNames)
        {
            Tables = tables ?? throw new ArgumentNullException(nameof(tables));
            _minLength = Math.Clamp(tables.MinLength, 3, 20);
            _maxLength = Math.Clamp(tables.MaxLength, _minLength, 20);
            // invalidnames.txt rules and the bot-only list (famous fictional names) are both "contains" checks
            Blocklist = invalidNames ?? new BotNameBlocklist([]);
            _extra = new BotNameBlocklist(Split(tables.BotOnlyBlocked).Where(x => x.Length > 0));
            Build();
        }

        private readonly BotNameBlocklist _extra;

        private bool IsBlocked(string lower) => Blocklist.IsBlocked(lower) || _extra.IsBlocked(lower);

        /// <summary>Checks the tables; null when they are usable.</summary>
        public static string Validate(BotNameTables tables)
        {
            if (tables == null)
                return "The file is empty.";
            foreach (eRace race in StyleRaces)
            {
                if (!tables.Races.TryGetValue(race.ToString(), out var style) || style == null)
                    return $"Race '{race}' is missing.";
                if (!Enum.TryParse(style.Realm, true, out eRealm realm) || realm != RealmOf(race))
                    return $"Race '{race}' must have realm '{RealmName(RealmOf(race))}'.";
                foreach (var (label, parts) in new[] { ("male", style.Male), ("female", style.Female) })
                    if (parts == null || Split(parts.Starts).Length == 0 || Split(parts.Ends).Length == 0)
                        return $"Race '{race}' {label} needs starts and ends.";
                foreach (string rule in Split(style.Variants))
                    if (!KnownRules.Contains(rule))
                        return $"Race '{race}' has an unknown variant '{rule}'.";
            }
            foreach (string extra in tables.Races.Keys)
                if (!StyleRaces.Any(r => r.ToString().Equals(extra, StringComparison.OrdinalIgnoreCase)))
                    return $"Race '{extra}' is not a Classic + SI race.";
            return null;
        }

        private void Build()
        {
            string problem = Validate(Tables);
            if (problem != null)
                throw new InvalidDataException(problem);
            var w = Tables.Weights ?? new BotNameTables.KindWeights();
            foreach (eRace race in StyleRaces)
            {
                var style = Tables.Races[race.ToString()];
                eRealm realm = RealmOf(race);
                string[] mids = Split(style.Mids);
                if (mids.Length == 0)
                    mids = [string.Empty];
                string[] bridges = Tables.Bridges.TryGetValue(RealmName(realm), out string b) ? Split(b).Where(x => x.Length > 0).ToArray() : [];
                string[] rules = Split(style.Variants).Distinct().ToArray();
                foreach (eGender gender in new[] { eGender.Male, eGender.Female })
                {
                    var parts = gender == eGender.Male ? style.Male : style.Female;
                    string[] starts = Split(parts.Starts), ends = Split(parts.Ends);
                    string g = gender == eGender.Male ? "M" : "F";
                    long sizeA = (long)starts.Length * mids.Length * ends.Length;
                    long sizeB = style.Long && bridges.Length > 0 ? sizeA * bridges.Length : 0;
                    var list = new List<SubPool>();
                    void Add(PoolKind kind, string suffix, long size, int weight, string[] useBridges, string[] useRules)
                    {
                        if (size <= 0)
                            return;
                        ulong sig = Fnv(string.Join("|", kind, string.Join(" ", starts), string.Join(" ", mids),
                            string.Join(" ", useBridges ?? []), string.Join(" ", ends), string.Join(" ", useRules ?? []), _minLength, _maxLength));
                        list.Add(new SubPool($"{race}/{g}/{suffix}", PoolIdFor(race, gender), realm, race, gender, kind, size, Math.Max(0, weight), sig)
                        {
                            Starts = starts, Mids = mids, Ends = ends, Bridges = useBridges, Rules = useRules,
                        });
                    }
                    Add(PoolKind.A, "a", sizeA, w.A, null, null);
                    Add(PoolKind.B, "b", sizeB, w.B, bridges, null);
                    if (rules.Length > 0)
                    {
                        Add(PoolKind.VariantA, "va", sizeA * rules.Length, w.VariantA, null, rules);
                        Add(PoolKind.VariantB, "vb", sizeB * rules.Length, w.VariantB, bridges, rules);
                    }
                    _pools[(race, gender)] = list.ToArray();
                }
            }
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                if (!Tables.Compounds.TryGetValue(RealmName(realm), out var c) || c == null)
                    continue;
                string[] first = Split(c.First).Where(x => x.Length > 0).ToArray(), second = Split(c.Second).Where(x => x.Length > 0).ToArray();
                long size = (long)first.Length * second.Length * 2;
                if (size == 0)
                    continue;
                ulong sig = Fnv(string.Join("|", PoolKind.Compound, string.Join(" ", first), string.Join(" ", second), _minLength, _maxLength));
                _compounds[realm] = new SubPool($"compound/{RealmName(realm)}", CompoundPoolId(realm), realm, eRace.Unknown, eGender.Neutral,
                    PoolKind.Compound, size, Math.Max(0, w.Compound), sig) { First = first, Second = second };
            }
        }

        /// <summary>The pools a bot of this race and gender draws from (its own four plus the realm's compounds).</summary>
        public IReadOnlyList<SubPool> PoolsFor(eRace race, eGender gender)
        {
            if (gender != eGender.Female)
                gender = eGender.Male;
            if (!_pools.TryGetValue((race, gender), out SubPool[] own))
                return [];
            return _compounds.TryGetValue(RealmOf(race), out SubPool compound) ? [.. own, compound] : own;
        }

        /// <summary>The name at an index of a pool (capitalised), or null when that index makes no acceptable name.</summary>
        public string Decode(SubPool pool, long index)
        {
            if (pool == null || index < 0 || index >= pool.Size)
                return null;
            string lower = DecodeLower(pool, index);
            return lower == null ? null : Capitalise(lower);
        }

        private string DecodeLower(SubPool pool, long index)
        {
            switch (pool.Kind)
            {
                case PoolKind.A:
                case PoolKind.B:
                {
                    string n = Template(pool, index);
                    return Ok(n) ? n : null;
                }
                case PoolKind.VariantA:
                case PoolKind.VariantB:
                {
                    int r = (int)(index % pool.Rules.Length);
                    string baseName = Template(pool, index / pool.Rules.Length);
                    if (!Ok(baseName))
                        return null;
                    string v = ApplyRule(baseName, pool.Rules[r]);
                    return v != null && v != baseName && Ok(v) ? v : null;
                }
                case PoolKind.Compound:
                {
                    bool doubled = index % 2 == 1;
                    long rest = index / 2;
                    int y = (int)(rest % pool.Second.Length);
                    int x = (int)(rest / pool.Second.Length);
                    string n = Collapse(pool.First[x] + pool.Second[y]);
                    if (doubled)
                    {
                        if (n.Length == 0 || "lnrdk".IndexOf(n[^1]) < 0)
                            return null;
                        n += n[^1];
                    }
                    return Ok(n) ? n : null;
                }
            }
            return null;
        }

        private static string Template(SubPool pool, long index)
        {
            bool b = pool.Kind is PoolKind.B or PoolKind.VariantB;
            int e = (int)(index % pool.Ends.Length);
            index /= pool.Ends.Length;
            int br = 0;
            if (b)
            {
                br = (int)(index % pool.Bridges.Length);
                index /= pool.Bridges.Length;
            }
            int m = (int)(index % pool.Mids.Length);
            int s = (int)(index / pool.Mids.Length);
            return Collapse(pool.Starts[s] + pool.Mids[m] + (b ? pool.Bridges[br] : string.Empty) + pool.Ends[e]);
        }

        private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

        internal static string ApplyRule(string l, string rule)
        {
            if (l.Length < 2)
                return null;
            char last = l[^1];
            switch (rule)
            {
                case "double": return "lnrstkd".IndexOf(last) >= 0 && IsVowel(l[^2]) ? l + last : null;   // Thor -> Thorr
                case "c2k": return l[0] == 'c' ? "k" + l[1..] : null;                                     // Cael -> Kael
                case "i2y":
                {
                    int i = l.IndexOf('i', 1);                                                            // Kaelin -> Kaelyn
                    return i > 0 ? l[..i] + "y" + l[(i + 1)..] : null;
                }
                case "ix": return last is 'x' or 'z' ? null : l + (IsVowel(last) ? "x" : "ix");          // Vex, Nibix
                case "az": return last is 'x' or 'z' ? null : l + (IsVowel(last) ? "z" : "az");
                case "son": return l.Length <= 7 ? l + "son" : null;
                case "mac": return l.Length <= 7 ? "mac" + l : null;
                case "o": return l.Length <= 7 && !IsVowel(l[0]) ? "o" + l : null;
            }
            return null;
        }

        /// <summary>The quality filter plus the blocklists, on a lower-case name.</summary>
        public bool Ok(string l)
        {
            if (!PassesQuality(l, _minLength, _maxLength))
                return false;
            return !IsBlocked(l);
        }

        /// <summary>Length, letters only, and readable: no triple letters, 4 consonants or 3 vowels in a row, no
        /// "ee/uu/yy", no "abab" stutter, no repeated 3- or 4-letter piece.</summary>
        public static bool PassesQuality(string l, int minLength = 4, int maxLength = 12)
        {
            if (l == null || l.Length < minLength || l.Length > maxLength)
                return false;
            int consonants = 0, vowels = 0;
            for (int i = 0; i < l.Length; i++)
            {
                char c = l[i];
                if (c < 'a' || c > 'z')
                    return false;
                if (i >= 2 && c == l[i - 1] && c == l[i - 2])
                    return false;
                if (IsVowel(c))
                {
                    consonants = 0;
                    if (++vowels >= 3)
                        return false;
                    if (i >= 1 && c == l[i - 1] && c is 'e' or 'u' or 'y')
                        return false;
                }
                else
                {
                    vowels = 0;
                    if (++consonants >= 4)
                        return false;
                }
            }
            for (int i = 0; i + 3 < l.Length; i++)
                if (l[i] == l[i + 2] && l[i + 1] == l[i + 3])
                    return false;
            for (int k = 3; k <= 4; k++)
                for (int i = 0; i < l.Length - k; i++)
                    if (l.IndexOf(l.Substring(i, k), i + k, StringComparison.Ordinal) >= 0)
                        return false;
            return true;
        }

        /// <summary>A random acceptable name for the race and gender, not claimed (hire window, /tg helpers, previews).
        /// <paramref name="reject"/> refuses names already used. Null when nothing was found.</summary>
        public string RandomName(eRace race, eGender gender, Random random, Func<string, bool> reject = null, int attempts = 400)
        {
            random ??= Random.Shared;
            IReadOnlyList<SubPool> pools = PoolsFor(race, gender);
            if (pools.Count == 0)
                return null;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                SubPool pool = PickWeighted(pools, random, null);
                if (pool == null)
                    return null;
                string name = Decode(pool, NextLong(random, pool.Size));
                if (name != null && (reject == null || !reject(name)))
                    return name;
            }
            // dense fallback: walk the plain template in order (only reached when the random draws keep hitting used names)
            foreach (SubPool pool in pools.Where(p => p.Kind is PoolKind.A or PoolKind.B))
                for (long i = 0; i < pool.Size; i++)
                {
                    string name = Decode(pool, i);
                    if (name != null && (reject == null || !reject(name)))
                        return name;
                }
            return null;
        }

        /// <summary>Weighted pick of a pool; <paramref name="usable"/> filters out exhausted ones.</summary>
        public static SubPool PickWeighted(IReadOnlyList<SubPool> pools, Random random, Func<SubPool, bool> usable)
        {
            long total = 0;
            int count = 0;
            foreach (SubPool p in pools)
                if (usable == null || usable(p))
                {
                    total += p.Weight;
                    count++;
                }
            if (count == 0)
                return null;
            if (total <= 0)
            {
                int pick = random.Next(count);
                foreach (SubPool p in pools)
                    if ((usable == null || usable(p)) && pick-- == 0)
                        return p;
                return null;
            }
            long roll = NextLong(random, total);
            foreach (SubPool p in pools)
            {
                if (usable != null && !usable(p) || p.Weight <= 0)
                    continue;
                if (roll < p.Weight)
                    return p;
                roll -= p.Weight;
            }
            return null;
        }

        private static long NextLong(Random random, long max) => max <= int.MaxValue ? random.Next((int)max) : random.NextInt64(max);

        // ---- Feistel order

        /// <summary>A bijection of [0, size): a 4-round Feistel network on the next even bit width, cycle-walked into range.
        /// The same key always gives the same order.</summary>
        public static long Permute(long index, long size, ulong key)
        {
            if (size <= 1 || index < 0 || index >= size)
                return Math.Max(0, Math.Min(index, size - 1));
            int bits = 64 - BitOperations.LeadingZeroCount((ulong)(size - 1));
            if (bits < 2)
                bits = 2;
            if ((bits & 1) == 1)
                bits++;
            int half = bits / 2;
            ulong mask = (1UL << half) - 1;
            ulong v = (ulong)index;
            do
            {
                ulong l = v >> half, r = v & mask;
                for (ulong round = 0; round < 4; round++)
                {
                    ulong f = Mix(r ^ (key + round * 0x9E3779B97F4A7C15UL)) & mask;
                    (l, r) = (r, l ^ f);
                }
                v = l << half | r;
            }
            while (v >= (ulong)size);
            return (long)v;
        }

        public static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static ulong Fnv(string text)
        {
            ulong h = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(text ?? string.Empty))
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            return h;
        }

        /// <summary>The walk key of a pool for one world (seed) and its current parts (signature).</summary>
        public static ulong KeyFor(SubPool pool, ulong worldSeed) => Mix(worldSeed ^ Fnv(pool.Key) ^ Mix(pool.Signature));

        // ---- helpers

        public static string[] Split(string list) =>
            string.IsNullOrWhiteSpace(list)
                ? []
                : list.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x == "-" ? string.Empty : x.ToLowerInvariant()).ToArray();

        public static string Collapse(string s) =>
            s.Replace("aa", "a", StringComparison.Ordinal).Replace("ii", "i", StringComparison.Ordinal)
             .Replace("uu", "u", StringComparison.Ordinal).Replace("yy", "y", StringComparison.Ordinal);

        public static string Capitalise(string lower) =>
            string.IsNullOrEmpty(lower) ? lower : char.ToUpperInvariant(lower[0]) + lower[1..];

        // ---- the built-in tables

        public const string FileName = "botnames165.json";

        /// <summary>The tables built into the server (GameServer\config\botnames165.json, embedded).</summary>
        public static string BuiltInJson()
        {
            using Stream stream = DOL.Config.ResourceUtil.GetResourceStream(FileName);
            if (stream == null)
                return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>Generator from the built-in tables (blocklist: the bot-only list plus <paramref name="invalidNames"/>).</summary>
        public static BotNameGenerator BuiltIn(BotNameBlocklist invalidNames = null) =>
            new(BotNameTables.Parse(BuiltInJson() ?? throw new InvalidDataException($"{FileName} is not embedded.")), invalidNames);
    }
}
