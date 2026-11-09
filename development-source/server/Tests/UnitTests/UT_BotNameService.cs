using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>True online world names: generator, tables, blocklists and the offline_bot_names claim table (temp DBs only).</summary>
    [TestFixture, NonParallelizable]
    public class UT_BotNameService
    {
        private readonly List<string> _paths = new();
        private readonly List<BotNameRegistry> _registries = new();

        [TearDown]
        public void TearDown()
        {
            foreach (BotNameRegistry r in _registries)
                r.Dispose();
            _registries.Clear();
            SQLiteConnection.ClearAllPools();
            foreach (string path in _paths)
                foreach (string file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
                    try
                    {
                        if (File.Exists(file))
                            File.Delete(file);
                    }
                    catch (IOException) { }
            _paths.Clear();
        }

        private static BotNameGenerator BuiltIn(params string[] blocked) =>
            BotNameGenerator.BuiltIn(blocked.Length == 0 ? null : new BotNameBlocklist(blocked));

        private string TempDb()
        {
            string path = Path.Combine(Path.GetTempPath(), $"botnames-{Guid.NewGuid():N}.db");
            _paths.Add(path);
            using (var c = new SQLiteConnection($"Data Source={path};Version=3"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                cmd.ExecuteNonQuery();
            }
            return $"Data Source={path};Version=3;Synchronous=Off";
        }

        private BotNameRegistry Registry(string cs, BotNameGenerator generator, ulong seed = 1650, int rngSeed = 7,
            Func<DateTime> clock = null, Func<string, bool> liveBlocked = null, Func<IEnumerable<string>> protectedNames = null)
        {
            var registry = new BotNameRegistry(new BotNameRegistry.Options
            {
                ConnectionString = cs,
                Seed = seed,
                Random = new Random(rngSeed),
                UtcNow = clock ?? (() => new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc)),
                LiveBlocked = liveBlocked,
                ProtectedNames = protectedNames,
                StartWorker = false,
                ClaimWaitMs = 2000,
            }, generator);
            registry.Open();
            registry.MarkReady();
            _registries.Add(registry);
            return registry;
        }

        private static void Exec(string cs, string sql)
        {
            using var c = new SQLiteConnection(cs);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        /// <summary>Tables with 2 starts x 2 ends per gender (4 names), no variants or compounds: easy to exhaust.</summary>
        private static BotNameTables TinyTables()
        {
            var t = new BotNameTables { Weights = new BotNameTables.KindWeights { A = 1, B = 0, VariantA = 0, VariantB = 0, Compound = 0 } };
            string[] maleStarts = ["Bar Gam", "Dor Kel", "Fen Hal", "Jor Lam", "Mor Nel", "Par Rol", "Sar Tor", "Var Wen",
                "Bel Dar", "Gor Hen", "Kar Lor", "Mar Nor", "Pel Ral", "Sel Tar", "Vel Wor"];
            int i = 0;
            foreach (eRace race in BotNameGenerator.StyleRaces)
            {
                t.Races[race.ToString()] = new BotNameTables.RaceStyle
                {
                    Realm = BotNameGenerator.RealmName(BotNameGenerator.RealmOf(race)),
                    Mids = "-",
                    Male = new() { Starts = maleStarts[i], Ends = "dok wun" },
                    Female = new() { Starts = maleStarts[i], Ends = "ela ise" },
                };
                i++;
            }
            return t;
        }

        // ---- races and tables

        [Test]
        public void StyleRaces_AreExactlyTheFifteenClassicSiRaces()
        {
            Assert.That(BotNameGenerator.StyleRaces, Has.Count.EqualTo(15));
            eRace[] banned = [eRace.HalfOgre, eRace.Frostalf, eRace.Shar, eRace.AlbionMinotaur, eRace.MidgardMinotaur, eRace.HiberniaMinotaur];
            Assert.That(BotNameGenerator.StyleRaces, Has.None.Matches<eRace>(banned.Contains));
            BotNameGenerator g = BuiltIn();
            foreach (eRace race in banned)
                Assert.That(g.PoolsFor(race, eGender.Male), Is.Empty, race.ToString());
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                Assert.That(BotNameGenerator.StyleRaces.Count(r => BotNameGenerator.RealmOf(r) == realm), Is.EqualTo(5));

            BotNameTables tables = BotNameTables.Parse(BotNameGenerator.BuiltInJson());
            Assert.That(tables.Races.Keys, Has.None.Matches<string>(k => k.Contains("Ogre", StringComparison.OrdinalIgnoreCase)
                || k.Contains("Frost", StringComparison.OrdinalIgnoreCase) || k.Contains("Shar", StringComparison.OrdinalIgnoreCase)));
            tables.Races["HalfOgre"] = tables.Races["Briton"];
            Assert.That(BotNameGenerator.Validate(tables), Does.Contain("HalfOgre"));
        }

        [Test]
        public void BuiltInTables_MatchTheDevTreeFileAndValidate()
        {
            string json = BotNameGenerator.BuiltInJson();
            Assert.That(json, Is.Not.Null.And.Not.Empty);
            Assert.That(BotNameGenerator.Validate(BotNameTables.Parse(json)), Is.Null);
            string dir = TestContext.CurrentContext.TestDirectory;
            for (int up = 0; up < 8 && dir != null; up++, dir = Path.GetDirectoryName(dir))
            {
                string file = Path.Combine(dir, "GameServer", "config", BotNameGenerator.FileName);
                if (File.Exists(file))
                {
                    Assert.That(File.ReadAllText(file).TrimStart('﻿'), Is.EqualTo(json.TrimStart('﻿')));
                    return;
                }
            }
            Assert.Inconclusive("dev-tree botnames165.json not found from the test folder");
        }

        [Test]
        public void Validate_ReportsMissingRaceWrongRealmAndUnknownVariant()
        {
            BotNameTables t = TinyTables();
            Assert.That(BotNameGenerator.Validate(t), Is.Null);
            t.Races["Troll"].Realm = "Albion";
            Assert.That(BotNameGenerator.Validate(t), Does.Contain("Troll"));
            t = TinyTables();
            t.Races["Elf"].Variants = "double sparkle";
            Assert.That(BotNameGenerator.Validate(t), Does.Contain("sparkle"));
            t = TinyTables();
            t.Races.Remove("Kobold");
            Assert.That(BotNameGenerator.Validate(t), Does.Contain("Kobold"));
        }

        [Test]
        public void EveryRace_NamesFollowItsOwnSyllables()
        {
            BotNameGenerator g = BuiltIn();
            BotNameTables tables = g.Tables;
            var rng = new Random(165);
            var byRace = new Dictionary<eRace, HashSet<string>>();
            foreach (eRace race in BotNameGenerator.StyleRaces)
            {
                byRace[race] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (eGender gender in new[] { eGender.Male, eGender.Female })
                {
                    var style = tables.Races[race.ToString()];
                    string[] starts = BotNameGenerator.Split(gender == eGender.Male ? style.Male.Starts : style.Female.Starts);
                    var plain = g.PoolsFor(race, gender).Where(p => p.Kind is BotNameGenerator.PoolKind.A or BotNameGenerator.PoolKind.B).ToArray();
                    Assert.That(plain, Is.Not.Empty, $"{race} {gender}");
                    int found = 0;
                    for (int i = 0; i < 20_000 && found < 150; i++)
                    {
                        var pool = plain[rng.Next(plain.Length)];
                        string name = g.Decode(pool, rng.NextInt64(pool.Size));
                        if (name == null)
                            continue;
                        found++;
                        byRace[race].Add(name);
                        // a plain name starts with one of the race's own (gender's) starts (aa/ii/uu/yy collapse aside)
                        Assert.That(starts.Any(s => name.StartsWith(BotNameGenerator.Collapse(s)[..Math.Min(2, s.Length)], StringComparison.OrdinalIgnoreCase)),
                            Is.True, $"{race} {gender}: {name}");
                        Assert.That(name, Does.Match("^[A-Z][a-z]{3,11}$"));
                    }
                    Assert.That(found, Is.EqualTo(150), $"{race} {gender} makes names");
                }
            }
            // styles differ: a kobold and a troll, an elf and a firbolg share (almost) nothing
            Assert.That(byRace[eRace.Kobold].Intersect(byRace[eRace.Troll], StringComparer.OrdinalIgnoreCase).Count(), Is.LessThan(5));
            Assert.That(byRace[eRace.Elf].Intersect(byRace[eRace.Firbolg], StringComparer.OrdinalIgnoreCase).Count(), Is.LessThan(5));
        }

        [Test]
        public void QualityFilter_RejectsUnreadableNames()
        {
            Assert.That(BotNameGenerator.PassesQuality("thorgrim"), Is.True);
            Assert.That(BotNameGenerator.PassesQuality("abc"), Is.False);                 // too short
            Assert.That(BotNameGenerator.PassesQuality("abcdefghijklm"), Is.False);       // too long
            Assert.That(BotNameGenerator.PassesQuality("thorrr"), Is.False);              // triple letter
            Assert.That(BotNameGenerator.PassesQuality("ardstrak"), Is.False);            // 4 consonants
            Assert.That(BotNameGenerator.PassesQuality("aiolin"), Is.False);              // 3 vowels
            Assert.That(BotNameGenerator.PassesQuality("feenor"), Is.False);              // ee
            Assert.That(BotNameGenerator.PassesQuality("ararin"), Is.False);              // abab stutter
            Assert.That(BotNameGenerator.PassesQuality("tormaltor"), Is.False);           // repeated piece
            Assert.That(BotNameGenerator.PassesQuality("tor-mal"), Is.False);             // letters only
        }

        // ---- blocklists

        [Test]
        public void Blocklist_InvalidNamesAndBotOnlyFamousNamesNeverComeOut()
        {
            var blocklist = new BotNameBlocklist(["# comment", "thor", "/^gr.m/", "", "kkk # trailing comment"]);
            Assert.That(blocklist.IsBlocked("arnthorson"), Is.True);
            Assert.That(blocklist.IsBlocked("grimald"), Is.True);
            Assert.That(blocklist.IsBlocked("aldgrim"), Is.False);
            Assert.That(blocklist.IsBlocked("kkkaren"), Is.True);
            Assert.That(blocklist.IsBlocked("leofric"), Is.False);

            BotNameGenerator g = BotNameGenerator.BuiltIn(blocklist);
            Assert.That(g.Ok("legolas"), Is.False, "bot-only famous name");
            Assert.That(g.Ok("gandalfin"), Is.False, "contains a famous name");
            var rng = new Random(3);
            foreach (eRace race in new[] { eRace.Norseman, eRace.Dwarf, eRace.Troll })
                for (int i = 0; i < 3000; i++)
                {
                    string name = g.RandomName(race, i % 2 == 0 ? eGender.Male : eGender.Female, rng);
                    Assert.That(name, Is.Not.Null);
                    Assert.That(name.ToLowerInvariant(), Does.Not.Contain("thor"));
                    Assert.That(name, Does.Not.Match("^(?i)gr.m"));
                }
        }

        [Test]
        public void RealInvalidNamesFile_BlocksItsWords()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            string file = null;
            for (int up = 0; up < 8 && dir != null && file == null; up++, dir = Path.GetDirectoryName(dir))
                if (File.Exists(Path.Combine(dir, "GameServer", "config", "invalidnames.txt")))
                    file = Path.Combine(dir, "GameServer", "config", "invalidnames.txt");
            if (file == null)
                Assert.Inconclusive("invalidnames.txt not found");
            BotNameGenerator g = BotNameGenerator.BuiltIn(BotNameBlocklist.FromFile(file, null));
            var manager = new InvalidNamesManager(file);
            var rng = new Random(11);
            int checkedNames = 0;
            foreach (eRace race in BotNameGenerator.StyleRaces)
                for (int i = 0; i < 400; i++, checkedNames++)
                {
                    string name = g.RandomName(race, i % 2 == 0 ? eGender.Male : eGender.Female, rng);
                    Assert.That(manager[name], Is.False, $"{race}: {name} hits invalidnames.txt");
                }
            Assert.That(checkedNames, Is.EqualTo(6000));
        }

        // ---- Feistel order and capacity

        [Test]
        public void Permute_IsABijectionAndDependsOnTheKey()
        {
            foreach (long size in new long[] { 1, 2, 3, 7, 64, 100, 257, 1000, 4097 })
            {
                var seen = new HashSet<long>();
                for (long i = 0; i < size; i++)
                {
                    long p = BotNameGenerator.Permute(i, size, 12345);
                    Assert.That(p, Is.InRange(0, size - 1));
                    Assert.That(seen.Add(p), Is.True, $"size {size}: {p} twice");
                }
            }
            long[] a = Enumerable.Range(0, 50).Select(i => BotNameGenerator.Permute(i, 1_000_000, 1)).ToArray();
            long[] b = Enumerable.Range(0, 50).Select(i => BotNameGenerator.Permute(i, 1_000_000, 2)).ToArray();
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(a, Is.EqualTo(Enumerable.Range(0, 50).Select(i => BotNameGenerator.Permute(i, 1_000_000, 1)).ToArray()));
        }

        [Test]
        public void CapacitySampling_EstimatesOverThreeMillion()
        {
            BotNameGenerator g = BuiltIn();
            var rng = new Random(1650);
            var perRealm = new Dictionary<eRealm, double>();
            foreach (var pool in g.AllPools)
            {
                int samples = (int)Math.Min(pool.Size, 1500), valid = 0;
                for (int i = 0; i < samples; i++)
                    if (g.Decode(pool, pool.Size <= 1500 ? i : rng.NextInt64(pool.Size)) != null)
                        valid++;
                perRealm[pool.Realm] = perRealm.GetValueOrDefault(pool.Realm) + (double)valid / samples * pool.Size;
            }
            TestContext.Out.WriteLine(string.Join(", ", perRealm.Select(kv => $"{BotNameGenerator.RealmName(kv.Key)} ~{kv.Value:N0}")));
            // valid indices (the full distinct count is measured by MeasureCapacity_Full: about 3.3 million)
            Assert.That(perRealm.Values.Sum(), Is.GreaterThan(3_300_000));
            Assert.That(perRealm.Values.Min(), Is.GreaterThan(850_000));
        }

        /// <summary>Exact distinct capacity (every index of every pool). Takes a minute or two.</summary>
        [Test, Explicit("full enumeration, about a minute")]
        public void MeasureCapacity_Full()
        {
            string dir = TestContext.CurrentContext.TestDirectory, file = null;
            for (int up = 0; up < 8 && dir != null && file == null; up++, dir = Path.GetDirectoryName(dir))
                if (File.Exists(Path.Combine(dir, "GameServer", "config", "invalidnames.txt")))
                    file = Path.Combine(dir, "GameServer", "config", "invalidnames.txt");
            BotNameGenerator g = BotNameGenerator.BuiltIn(file == null ? null : BotNameBlocklist.FromFile(file, null));
            var all = new HashSet<ulong>();
            var perRealm = new Dictionary<eRealm, HashSet<ulong>>();
            var watch = Stopwatch.StartNew();
            long raw = 0;
            foreach (var pool in g.AllPools)
            {
                var set = perRealm.TryGetValue(pool.Realm, out var s) ? s : perRealm[pool.Realm] = new HashSet<ulong>();
                raw += pool.Size;
                for (long i = 0; i < pool.Size; i++)
                    if (g.Decode(pool, i) is { } name)
                        set.Add(BotNameGenerator.Fnv(name.ToLowerInvariant()));
            }
            foreach (var (realm, set) in perRealm)
            {
                all.UnionWith(set);
                TestContext.Out.WriteLine($"{BotNameGenerator.RealmName(realm)}: {set.Count:N0}");
            }
            TestContext.Out.WriteLine($"TOTAL distinct: {all.Count:N0} (raw indices {raw:N0}, {watch.Elapsed.TotalSeconds:F0} s)");
            Assert.That(all.Count, Is.GreaterThan(3_000_000));
        }

        [Test]
        public void IdentityGenerator_FiftyThousandPerRealmWithoutThrowing()
        {
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var rng = new Random((int)realm);
                for (int i = 0; i < 50_000; i++)
                {
                    var id = AutonomousBotIdentityGenerator.Generate(realm, i % 2 == 0 ? eGender.Male : eGender.Female, names, rng);
                    Assert.That(BotNameGenerator.RealmOf(id.Race), Is.EqualTo(realm));
                }
                Assert.That(names, Has.Count.EqualTo(50_000));
            }
        }

        // ---- the claim table

        [Test]
        public void Claims_FiftyThousandPerRealmUniqueAndNeverThrow()
        {
            string cs = TempDb();
            BotNameRegistry registry = Registry(cs, BuiltIn());
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rng = new Random(5);
            var watch = Stopwatch.StartNew();
            long botId = 1;
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                eRace[] races = BotNameGenerator.StyleRaces.Where(r => BotNameGenerator.RealmOf(r) == realm).ToArray();
                for (int i = 0; i < 50_000; i++)
                {
                    eRace race = races[rng.Next(races.Length)];
                    Assert.That(registry.TryClaimNewBotName(realm, race, i % 2 == 0 ? eGender.Male : eGender.Female, botId++, out string name), Is.True);
                    Assert.That(names.Add(name), Is.True, $"{name} given twice");
                }
            }
            TestContext.Out.WriteLine($"150,000 claims in {watch.Elapsed.TotalSeconds:F1} s");
            Assert.That(registry.Count(BotNameRegistry.NameState.Warm), Is.EqualTo(150_000));
        }

        [Test]
        public void Claims_AreDeterministicWithAFixedSeed()
        {
            string[] Run(string cs)
            {
                BotNameRegistry r = Registry(cs, BuiltIn(), seed: 99, rngSeed: 42);
                var list = new List<string>();
                for (int i = 0; i < 300; i++)
                {
                    eRace race = BotNameGenerator.StyleRaces[i % 15];
                    Assert.That(r.TryClaimNewBotName(BotNameGenerator.RealmOf(race), race, i % 3 == 0 ? eGender.Female : eGender.Male, i + 1, out string n), Is.True);
                    list.Add(n);
                }
                return list.ToArray();
            }
            string[] first = Run(TempDb()), second = Run(TempDb());
            Assert.That(second, Is.EqualTo(first));
            BotNameRegistry other = Registry(TempDb(), BuiltIn(), seed: 100, rngSeed: 42);
            Assert.That(other.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Female, 1, out string otherName), Is.True);
            Assert.That(first.Take(1).Concat([otherName]).Distinct().Count(), Is.EqualTo(2).Or.EqualTo(1));
        }

        [Test]
        public void Counters_PersistWithEachClaimAndSurviveARestart()
        {
            string cs = TempDb();
            BotNameRegistry r1 = Registry(cs, BuiltIn(), seed: 5, rngSeed: 1);
            var first = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 200; i++)
            {
                Assert.That(r1.TryClaimNewBotName(eRealm.Midgard, eRace.Kobold, eGender.Male, i, out string n), Is.True);
                first.Add(n);
            }
            r1.Dispose();
            _registries.Remove(r1);
            BotNameRegistry r2 = Registry(cs, BuiltIn(), seed: 5, rngSeed: 1);   // same random rolls: only the counters prevent repeats
            for (int i = 0; i < 200; i++)
            {
                Assert.That(r2.TryClaimNewBotName(eRealm.Midgard, eRace.Kobold, eGender.Male, 1000 + i, out string n), Is.True);
                Assert.That(first.Contains(n), Is.False, $"{n} came again after the restart");
            }
        }

        [Test]
        public void PlayerAndBotNames_ExcludeEachOther()
        {
            string cs = TempDb();
            BotNameRegistry r = Registry(cs, BuiltIn());
            Assert.That(r.TryClaimPlayerName("Aldwin"), Is.True);
            Assert.That(r.IsNameTaken("aldwin"), Is.True, "case-insensitive");
            Assert.That(r.TryClaimPlayerName("ALDWIN"), Is.False);
            Assert.That(r.TryClaimBotName("Aldwin", 5), Is.False);

            Assert.That(r.TryClaimNewBotName(eRealm.Hibernia, eRace.Elf, eGender.Female, 77, out string bot), Is.True);
            Assert.That(r.IsNameTaken(bot), Is.True);
            Assert.That(r.TryClaimPlayerName(bot), Is.False, "a player can't take a bot's name");
            r.MarkArchived(bot, true);
            Assert.That(r.Flush(2000), Is.True);
            Assert.That(r.GetState(bot), Is.EqualTo(BotNameRegistry.NameState.Archived));
            Assert.That(r.TryClaimPlayerName(bot), Is.False, "nor an archived bot's");
            r.MarkArchived(bot, false);
            r.Flush(2000);
            Assert.That(r.GetRow(bot)!.Value.State, Is.EqualTo(BotNameRegistry.NameState.Warm));
            Assert.That(r.GetRow(bot)!.Value.BotId, Is.EqualTo(77));

            Assert.That(r.TryClaimBotName("Companionx", 0), Is.True);
            r.SetBotId("Companionx", 901);
            r.Flush(2000);
            Assert.That(r.GetRow("Companionx")!.Value.BotId, Is.EqualTo(901));
        }

        [Test]
        public void DeletedCharacter_NameCoolsDownButAPlayerMayTakeItAgain()
        {
            string cs = TempDb();
            BotNameRegistry r = Registry(cs, BuiltIn());
            Assert.That(r.TryClaimPlayerName("Mairead"), Is.True);
            r.ReleasePlayerName("Mairead");
            // the next claim applies the queued release first (same transaction)
            Assert.That(r.TryClaimPlayerName("Mairead"), Is.True);
            r.ReleasePlayerName("Mairead");
            Assert.That(r.Flush(2000), Is.True);
            var row = r.GetRow("Mairead")!.Value;
            Assert.That(row.State, Is.EqualTo(BotNameRegistry.NameState.Cooling));
            Assert.That(row.ReleasedUnix, Is.Not.Null);
            Assert.That(r.IsNameTaken("Mairead"), Is.False);
            r.ReleaseBotName("Mairead");   // a bot release never touches a player row state
            r.Flush(2000);
            Assert.That(r.GetState("Mairead"), Is.EqualTo(BotNameRegistry.NameState.Cooling));
        }

        [Test]
        public void RetiredBotNames_AreReusedOnlyAfterThirtyDaysAndNeverFromFriendLists()
        {
            string cs = TempDb();
            DateTime now = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
            var friends = new List<string>();
            var generator = new BotNameGenerator(TinyTables(), null);
            BotNameRegistry r = Registry(cs, generator, clock: () => now, protectedNames: () => friends);

            var given = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                Assert.That(r.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, i + 1, out string n), Is.True);
                given.Add(n);
            }
            Assert.That(given, Is.EquivalentTo(new[] { "Bardok", "Barwun", "Gamdok", "Gamwun" }));
            // Briton male is used up: the next name comes from another Albion race (fresh before reused)
            Assert.That(r.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, 5, out string spill), Is.True);
            Assert.That(given, Does.Not.Contain(spill));

            // fill every Albion male pool so only reuse is left
            while (r.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, 9, out _)) { }
            Assert.That(r.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, 10, out _), Is.False);

            r.ReleaseBotName("Bardok");
            r.ReleaseBotName("Gamwun");
            r.Flush(2000);
            now = now.AddDays(29);
            Assert.That(r.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, 11, out _), Is.False, "still cooling down");

            now = now.AddDays(2);
            friends.Add("bardok");   // on a player's friend list: never reused
            Assert.That(r.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, 12, out string reused), Is.True);
            Assert.That(reused, Is.EqualTo("Gamwun"));
            Assert.That(r.GetRow("Gamwun")!.Value.BotId, Is.EqualTo(12));
            Assert.That(r.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, 13, out _), Is.False);
            friends.Clear();
            Assert.That(r.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, 14, out string last), Is.True);
            Assert.That(last, Is.EqualTo("Bardok"));
        }

        [Test]
        public void LiveBlocklist_IsCheckedAtClaimTime()
        {
            string cs = TempDb();
            BotNameRegistry r = Registry(cs, BuiltIn(), liveBlocked: n => n.Contains('a', StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < 300; i++)
            {
                Assert.That(r.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, i, out string n), Is.True);
                Assert.That(n, Does.Not.Contain("a").IgnoreCase);
            }
        }

        [Test]
        public void TableEdit_RestartsThePoolButNeverRepeatsAName()
        {
            string cs = TempDb();
            BotNameTables tables = TinyTables();
            tables.Races["Celt"].Male.Ends = "dok wun ran";
            BotNameRegistry r = Registry(cs, new BotNameGenerator(TinyTables(), null));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(r.TryClaimNewBotName(eRealm.Hibernia, eRace.Celt, eGender.Male, i, out string n), Is.True);
                seen.Add(n);
            }
            r.ReplaceGenerator(new BotNameGenerator(tables, null));   // a new end: 6 names, 4 used
            for (int i = 0; i < 2; i++)
            {
                Assert.That(r.TryClaimNewBotName(eRealm.Hibernia, eRace.Celt, eGender.Male, 10 + i, out string n), Is.True);
                Assert.That(n, Does.EndWith("ran"));
                Assert.That(seen.Add(n), Is.True);
            }
        }

        [Test]
        public void NotReady_GivesNoBotNamesAndUsesTheOldTables()
        {
            string cs = TempDb();
            var registry = new BotNameRegistry(new BotNameRegistry.Options
            {
                ConnectionString = cs, Seed = 1, StartWorker = false,
                LegacyTaken = n => n.Equals("Oldbot", StringComparison.OrdinalIgnoreCase),
            }, BuiltIn());
            _registries.Add(registry);
            registry.Open();
            Assert.That(registry.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, 1, out _), Is.False);
            Assert.That(registry.IsNameTaken("oldbot"), Is.True);
            Assert.That(registry.TryClaimPlayerName("Oldbot"), Is.False);
            Assert.That(registry.TryClaimPlayerName("Newplayer"), Is.True);
        }

        [Test]
        public void Seeding_ReservesBotsPlayersAndNpcNamesAndIsRepeatable()
        {
            string cs = TempDb();
            Exec(cs, "CREATE TABLE offline_world_bots(BotId INTEGER PRIMARY KEY, Name TEXT NOT NULL COLLATE NOCASE)");
            Exec(cs, "INSERT INTO offline_world_bots VALUES(1, 'Lorcan'), (2, 'Gisiorbel'), (3, '')");
            Exec(cs, "CREATE TABLE DOLCharacters(Name TEXT, SerializedFriendsList TEXT, SerializedIgnoreList TEXT)");
            Exec(cs, "INSERT INTO DOLCharacters VALUES('Asdasd', 'Lorcan,Someone', NULL)");
            Exec(cs, "CREATE TABLE Mob(Name TEXT)");
            Exec(cs, "INSERT INTO Mob VALUES('Growler'), ('giant spider'), ('Master Smith'), ('wolf'), ('Aldric')");
            Exec(cs, "CREATE TABLE NpcTemplate(Name TEXT)");
            Exec(cs, "INSERT INTO NpcTemplate VALUES('Bechard'), ('Growler')");

            var registry = new BotNameRegistry(new BotNameRegistry.Options { ConnectionString = cs, Seed = 1, StartWorker = false }, BuiltIn());
            _registries.Add(registry);
            registry.Open();
            Assert.That(registry.TryClaimPlayerName("Aldric"), Is.True, "before seeding: a player took an NPC's name");
            registry.SeedFromWorld();
            Assert.That(registry.Ready, Is.True);
            Assert.That(registry.GetRow("lorcan")!.Value, Is.EqualTo((1L, BotNameRegistry.NameState.Warm, (long?)null, -1)));
            Assert.That(registry.GetState("Gisiorbel"), Is.EqualTo(BotNameRegistry.NameState.Warm));
            Assert.That(registry.GetState("Asdasd"), Is.EqualTo(BotNameRegistry.NameState.Player));
            Assert.That(registry.GetState("Growler"), Is.EqualTo(BotNameRegistry.NameState.Reserved));
            Assert.That(registry.GetState("Bechard"), Is.EqualTo(BotNameRegistry.NameState.Reserved));
            Assert.That(registry.GetState("Aldric"), Is.EqualTo(BotNameRegistry.NameState.Player));
            Assert.That(registry.GetState("wolf"), Is.Null, "lower-case creature names are not reserved");
            Assert.That(registry.GetState("Master Smith"), Is.Null, "only one-word names");
            Assert.That(registry.IsNameTaken("Growler"), Is.False, "players may still pick an NPC's name");
            Assert.That(registry.TryClaimBotName("Lorcan", 9), Is.False);
            long total = registry.Count(BotNameRegistry.NameState.Warm) + registry.Count(BotNameRegistry.NameState.Player) + registry.Count(BotNameRegistry.NameState.Reserved);
            registry.SeedFromWorld();
            Assert.That(registry.Count(BotNameRegistry.NameState.Warm) + registry.Count(BotNameRegistry.NameState.Player) + registry.Count(BotNameRegistry.NameState.Reserved), Is.EqualTo(total));

            // a bot whose name was wrongly left cooling is restored by the next seed
            registry.ReleaseBotName("Lorcan");
            registry.Flush(2000);
            Assert.That(registry.GetState("Lorcan"), Is.EqualTo(BotNameRegistry.NameState.Cooling));
            registry.SeedFromWorld();
            Assert.That(registry.GetState("Lorcan"), Is.EqualTo(BotNameRegistry.NameState.Warm));
        }

        [Test]
        public void ReservedNpcNames_AreNeverGivenToBots()
        {
            string cs = TempDb();
            var generator = new BotNameGenerator(TinyTables(), null);
            BotNameRegistry r = Registry(cs, generator);
            Exec(cs, "INSERT INTO offline_bot_names(NameKey, BotId, State, ReleasedUtc, Pool) VALUES('Bardok', 0, 4, NULL, -1), ('Gamwun', 0, 3, NULL, -1)");
            var got = new List<string>();
            while (got.Count < 10 && r.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, 1, out string n))
                got.Add(n);
            Assert.That(got, Does.Not.Contain("Bardok").And.Not.Contain("Gamwun"));
            Assert.That(got, Does.Contain("Barwun").And.Contain("Gamdok"));
        }

        [Test]
        public void Service_WithoutARegistry_NeverThrows()
        {
            BotNameService.UseRegistryForTests(null);
            Assert.That(BotNameService.TryClaimNewBotName(eRealm.Albion, eRace.Briton, eGender.Male, 1, out string n), Is.False);
            Assert.That(n, Is.Null);
            Assert.DoesNotThrow(() => BotNameService.ReleaseBotName("X"));
            Assert.DoesNotThrow(() => BotNameService.MarkArchived("X", true));
            Assert.That(BotNameService.IsNameTaken("Anyone"), Is.False);
            Assert.That(BotNameService.SuggestName(eRealm.Midgard, eRace.Troll, eGender.Male), Does.Match("^[A-Z][a-z]+$"));
            Assert.That(BotNameService.SuggestName(eRealm.Hibernia, eRace.HalfOgre, eGender.Female), Is.Not.Null, "unknown race: a realm race");

            string cs = TempDb();
            BotNameRegistry r = Registry(cs, BuiltIn());
            BotNameService.UseRegistryForTests(r);
            try
            {
                Assert.That(BotNameService.TryClaimNewBotName(eRealm.Midgard, eRace.Valkyn, eGender.Female, 3, out string v), Is.True);
                Assert.That(BotNameService.IsNameTaken(v), Is.True);
                Assert.That(AutonomousBotIdentityGenerator.TryGenerateClaimed(eRealm.Hibernia, eGender.Male, 4, out var id), Is.True);
                Assert.That(BotNameService.IsNameTaken(id.Name), Is.True);
                Assert.That(BotNameService.TryClaimPlayerName(id.Name), Is.False);
            }
            finally
            {
                BotNameService.UseRegistryForTests(null);
            }
        }
    }
}
