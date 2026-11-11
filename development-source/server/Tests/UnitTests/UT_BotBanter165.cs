using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Data-driven bot banter (owner 2026-11-10): every banter165 file must load clean; pure picking rules.</summary>
[TestFixture]
public class UT_BotBanter165
{
    // The repo's banter165 folder (development-source\server\banter165), or BANTER165_DIR to check another copy.
    private static string Folder()
    {
        string overridden = Environment.GetEnvironmentVariable("BANTER165_DIR");
        if (!string.IsNullOrWhiteSpace(overridden))
            return overridden;
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, BanterLibrary165.FolderName);
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(dir.FullName, "GameServer")))
                return candidate;
        }
        return null;
    }

    private static BanterLibrary165.BanterSet Parse(string json)
    {
        var set = new BanterLibrary165.BanterSet();
        BanterLibrary165.AddFile(set, "test.json", json);
        return set;
    }

    private static string File1(string linesJson) => "{\"category\":\"test\",\"lines\":[" + linesJson + "]}";

    private static IReadOnlyDictionary<string, string> Values(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    [Test]
    public void EveryBanterFile_LoadsWithoutProblems()
    {
        string folder = Folder();
        Assert.That(folder, Is.Not.Null, "banter165 folder not found above the test directory");
        Assert.That(Directory.GetFiles(folder, "*.json"), Is.Not.Empty);
        BanterLibrary165.BanterSet set = BanterLibrary165.LoadFolder(folder);
        Assert.That(set.Errors, Is.Empty, string.Join(Environment.NewLine, set.Errors.Take(60)));
        Assert.That(set.Count, Is.GreaterThan(0));
        foreach (string situation in set.BySituation.Keys)
            Assert.That(BanterLibrary165.Situations.ContainsKey(situation), situation);
    }

    [Test]
    public void EveryBanterFile_HasSaneLinesAndNoDuplicates()
    {
        string folder = Folder();
        Assert.That(folder, Is.Not.Null);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(folder, "*.json").Where(p => !Path.GetFileName(p).StartsWith('_')))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement line in document.RootElement.GetProperty("lines").EnumerateArray())
            {
                string text = line.GetProperty("text").GetString();
                Assert.That(text.Length, Is.InRange(BanterLibrary165.MinLength, BanterLibrary165.MaxLength), text);
                Assert.That(seen.Add(BanterLibrary165.Normalize(text)), Is.True, $"duplicate line: {text}");
            }
        }
    }

    [Test]
    public void Parse_AcceptsAFullLineAndAppliesDefaults()
    {
        BanterLibrary165.BanterSet set = Parse("{\"category\":\"test\",\"defaults\":{\"channel\":\"guild\"},\"lines\":[" +
            "{\"text\":\"Hold {keep} for the realm!\",\"situation\":[\"rvr_defend\",\"rvr_siege\"],\"realm\":\"Midgard\",\"level\":[20,50],\"classType\":[\"tank\"],\"kind\":\"open\",\"weight\":2.5}]}");
        Assert.That(set.Errors, Is.Empty, string.Join("; ", set.Errors));
        BanterLibrary165.Line line = set.BySituation["rvr_defend"].Single();
        Assert.That(set.BySituation["rvr_siege"].Single(), Is.SameAs(line));
        Assert.That(line.ChannelMask, Is.EqualTo(BanterLibrary165.ChannelBit("guild")));
        Assert.That(line.RealmMask, Is.EqualTo(BanterLibrary165.RealmBit(eRealm.Midgard)));
        Assert.That((line.MinLevel, line.MaxLevel, line.Weight, line.Reply), Is.EqualTo((20, 50, 2.5, false)));
        Assert.That(line.Placeholders, Is.EquivalentTo(new[] { "keep" }));
    }

    [TestCase("{\"text\":\"Hello there\",\"situation\":\"nope\"}", "unknown situation")]
    [TestCase("{\"text\":\"Hello {nobody}\",\"situation\":\"idle\"}", "unknown placeholder")]
    [TestCase("{\"text\":\"Hello {player}\",\"situation\":\"idle\"}", "never filled")]
    [TestCase("{\"text\":\"Hello {Zone}\",\"situation\":\"idle\"}", "stray")]
    [TestCase("{\"text\":\"You noob, go home\",\"situation\":\"idle\"}", "forbidden word")]
    [TestCase("{\"text\":\"Off to Atlantis we go\",\"situation\":\"idle\"}", "forbidden word")]
    [TestCase("{\"text\":\"Grinding my master levels\",\"situation\":\"idle\"}", "forbidden phrase")]
    [TestCase("{\"text\":\"Fellow bots, unite\",\"situation\":\"idle\"}", "forbidden word")]
    [TestCase("{\"text\":\"Curly ’quote’ here\",\"situation\":\"idle\"}", "ASCII")]
    [TestCase("{\"text\":\"Hi\",\"situation\":\"idle\"}", "characters")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":\"idle\",\"channel\":\"shout\"}", "unknown channel")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":\"idle\",\"channel\":\"group\"}", "group channel")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":\"idle\",\"level\":[30,10]}", "level")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":\"idle\",\"kind\":\"maybe\"}", "kind")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":\"reply_greeting\",\"kind\":\"reply\"}", "kind")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":[\"idle\",\"reply_greeting\"]}", "mixed")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":\"idle\",\"weight\":0}", "weight")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":\"idle\",\"colour\":\"red\"}", "unknown key")]
    [TestCase("{\"text\":\"Glad to join\",\"situation\":\"guild_join\",\"channel\":\"realm\"}", "guild channel only")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":\"idle\",\"only\":[\"Spindelhalla\"]}", "must be an object")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":\"idle\",\"only\":{\"level\":[\"50\"]}}", "cannot test")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":\"idle\",\"only\":{\"boss\":[\"Golestandt\"]}}", "never filled")]
    [TestCase("{\"text\":\"Hello there\",\"situation\":\"idle\",\"only\":{\"zone\":[\" Spindelhalla\"]}}", "spaces around")]
    public void Parse_RejectsBadLines(string line, string expected)
    {
        BanterLibrary165.BanterSet set = Parse(File1(line));
        Assert.That(set.Errors.Any(e => e.Contains(expected, StringComparison.OrdinalIgnoreCase)), Is.True,
            $"expected '{expected}' in: {string.Join("; ", set.Errors)}");
        Assert.That(set.Count, Is.EqualTo(0));
    }

    [Test]
    public void Parse_RejectsDuplicatesAndAWrongCategory()
    {
        BanterLibrary165.BanterSet set = Parse("{\"category\":\"other\",\"lines\":[{\"text\":\"Good hunting!\",\"situation\":\"idle\"},{\"text\":\"good  hunting\",\"situation\":\"town\"}]}");
        Assert.That(set.Errors.Any(e => e.Contains("category")), Is.True);
        Assert.That(set.Errors.Any(e => e.Contains("duplicate")), Is.True);
        Assert.That(set.Count, Is.EqualTo(1));
    }

    [Test]
    public void Choose_HonoursChannelRealmLevelClassKindAndPlaceholders()
    {
        BanterLibrary165.BanterSet set = Parse(File1(string.Join(",",
            "{\"text\":\"Guild only line\",\"situation\":\"idle\",\"channel\":\"guild\"}",
            "{\"text\":\"Hibernia only line\",\"situation\":\"idle\",\"realm\":\"Hibernia\"}",
            "{\"text\":\"High level line\",\"situation\":\"idle\",\"level\":[40,50]}",
            "{\"text\":\"Healer only line\",\"situation\":\"idle\",\"classType\":\"healer\"}",
            "{\"text\":\"A reply line\",\"situation\":\"idle\",\"kind\":\"reply\"}",
            "{\"text\":\"Off to {destination} now\",\"situation\":\"idle\"}",
            "{\"text\":\"Plain open line\",\"situation\":\"idle\"}")));
        Assert.That(set.Errors, Is.Empty, string.Join("; ", set.Errors));
        var query = new BanterLibrary165.Query(["idle"], "realm", eRealm.Albion, 10, "Armsman", false, Values(("zone", "Camelot Hills")));
        var said = new HashSet<string>();
        for (int seed = 0; seed < 200; seed++)
            said.Add(BanterLibrary165.Choose(set, query, new Random(seed), null, out _).Text);
        Assert.That(said, Is.EquivalentTo(new[] { "Plain open line" }));

        var healer = query with { Channel = "guild", Realm = eRealm.Hibernia, Level = 45, ClassName = "Druid", Values = Values(("destination", "Tir na Nog")) };
        Assert.That(BanterLibrary165.CountCandidates(set, healer, out string situation), Is.EqualTo(6));
        Assert.That(situation, Is.EqualTo("idle"));
        Assert.That(BanterLibrary165.CountCandidates(set, healer with { Reply = true }, out _), Is.EqualTo(1));
    }

    [Test]
    public void Choose_SaysAnOnlyLineJustWhereItIsTrue()
    {
        // a line about one place, monster or class is never said about another
        BanterLibrary165.BanterSet set = Parse(File1(
            "{\"text\":\"These halls are full of spiders.\",\"situation\":\"dungeon\",\"only\":{\"zone\":[\"Spindelhalla\",\"Varulvhamn\"]}}," +
            "{\"text\":\"A Skald sings the whole way down.\",\"situation\":\"dungeon\",\"only\":{\"class\":\"Skald\",\"zone\":\"Spindelhalla\"}}," +
            "{\"text\":\"Mind the big one in the last room.\",\"situation\":\"dungeon\",\"only\":{\"target\":\"Pywrtr\"}}"));
        Assert.That(set.Errors, Is.Empty, string.Join("; ", set.Errors));
        var rng = new Random(5);
        BanterLibrary165.Query In(string zone, string className, string target = null) => new(["dungeon"], "say", eRealm.Midgard, 30, className, null,
            target == null ? Values(("zone", zone), ("class", className)) : Values(("zone", zone), ("class", className), ("target", target)));

        Assert.That(BanterLibrary165.CountCandidates(set, In("Spindelhalla", "Warrior"), out _), Is.EqualTo(1));
        Assert.That(BanterLibrary165.CountCandidates(set, In("spindelhalla", "Skald"), out _), Is.EqualTo(2), "names are compared without case");
        Assert.That(BanterLibrary165.CountCandidates(set, In("Varulvhamn", "Skald"), out _), Is.EqualTo(1), "every test of a line must hold");
        Assert.That(BanterLibrary165.CountCandidates(set, In("Nisse's Lair", "Skald"), out _), Is.Zero);
        Assert.That(BanterLibrary165.CountCandidates(set, In("Nisse's Lair", "Skald", "Pywrtr"), out _), Is.EqualTo(1));
        Assert.That(BanterLibrary165.Choose(set, In("Nisse's Lair", "Skald", "pywrtr"), rng, null, out _).Text, Is.EqualTo("Mind the big one in the last room."));
        // no value to test (the speaker is not in a zone the query knows): the line is not said
        var noZone = new BanterLibrary165.Query(["dungeon"], "say", eRealm.Midgard, 30, "Skald", null, Values(("class", "Skald")));
        Assert.That(BanterLibrary165.Choose(set, noZone, rng, null, out _), Is.Null);
    }

    [Test]
    public void Choose_FallsThroughSituationsInOrderAndAvoidsRecentLines()
    {
        BanterLibrary165.BanterSet set = Parse(File1(string.Join(",",
            "{\"text\":\"Idle line one\",\"situation\":\"idle\"}",
            "{\"text\":\"Idle line two\",\"situation\":\"idle\"}")));
        var query = new BanterLibrary165.Query(["rvr_siege", "idle"], "realm", eRealm.Albion, 30, "Cleric", false, Values());
        BanterLibrary165.Line first = BanterLibrary165.Choose(set, query, new Random(1), null, out string situation);
        Assert.That(situation, Is.EqualTo("idle"));
        for (int seed = 0; seed < 50; seed++)
            Assert.That(BanterLibrary165.Choose(set, query, new Random(seed), id => id == first.Id, out _), Is.Not.SameAs(first));
        Assert.That(BanterLibrary165.Choose(set, query with { Situations = ["rvr_siege"] }, new Random(1), null, out _), Is.Null);
    }

    [Test]
    public void Fill_ReplacesTokens()
    {
        Assert.That(BanterLibrary165.Fill("For {realm}! To {keep}!", Values(("realm", "Albion"), ("keep", "Caer Benowyc"))),
            Is.EqualTo("For Albion! To Caer Benowyc!"));
    }

    [Test]
    public void UseLibraryChance_KeepsOldLinesInTheMix()
    {
        Assert.That(BanterLibrary165.UseLibraryChance(0), Is.EqualTo(0));
        Assert.That(BanterLibrary165.UseLibraryChance(1), Is.InRange(0.5, 0.6));
        Assert.That(BanterLibrary165.UseLibraryChance(500), Is.EqualTo(0.85));
        Assert.That(BanterLibrary165.BuiltInCovers("idle"), Is.True);
        Assert.That(BanterLibrary165.BuiltInCovers("rvr_siege"), Is.False);
    }

    [Test]
    public void ResolveAmbient_EventsBeatPlacesAndIdleIsLast()
    {
        var random = new Random(4);
        Assert.That(BanterLibrary165.ResolveAmbient(new BanterLibrary165.Facts(RaidState: "boss", Dungeon: true), random)[0], Is.EqualTo("raid_boss"));
        List<string> defend = BanterLibrary165.ResolveAmbient(new BanterLibrary165.Facts(RvrRole: "defend", Frontier: true), random);
        Assert.That(defend[0], Is.EqualTo("rvr_defend"));
        Assert.That(defend, Does.Not.Contain("rvr_patrol"));
        Assert.That(BanterLibrary165.ResolveAmbient(new BanterLibrary165.Facts(Battleground: "Thidranki"), random).Take(2),
            Is.EqualTo(new[] { "bg_thidranki", "battleground" }));
        Assert.That(BanterLibrary165.ResolveAmbient(new BanterLibrary165.Facts(Frontier: true), random)[0], Is.EqualTo("rvr_patrol"));
        Assert.That(BanterLibrary165.ResolveAmbient(new BanterLibrary165.Facts(GroupSize: 4, HasTarget: true), random),
            Is.EqualTo(new List<string> { "group_hunt", "hunting", "idle" }));
        for (int seed = 0; seed < 20; seed++)
            Assert.That(BanterLibrary165.ResolveAmbient(new BanterLibrary165.Facts(), new Random(seed)).Last(), Is.EqualTo("idle"));
        foreach (string key in BanterLibrary165.ResolveAmbient(new BanterLibrary165.Facts("Caledonia", "forming", "relic", true, true, true, true, true, true, true, 3, true, true, true), new Random(0)))
            Assert.That(BanterLibrary165.Situations.ContainsKey(key), key);
    }

    [TestCase("hello everyone", false, "reply_greeting")]
    [TestCase("thanks for the help!", false, "reply_thanks")]
    [TestCase("DING 24!", false, "reply_ding")]
    [TestCase("anyone want to group up", false, "reply_lfg")]
    [TestCase("where is the trainer?", false, "reply_where")]
    [TestCase("anyone going to thidranki tonight", false, "reply_bg")]
    [TestCase("is the dragon raid up?", false, "reply_raid")]
    [TestCase("you are all dumb idiots", false, "reply_insult")]
    [TestCase("who won the election", false, "reply_offtopic")]
    [TestCase("anyone watch the epic football game", false, "reply_offtopic")]
    [TestCase("ty for the rez", false, "reply_thanks")]
    [TestCase("Aldric stop talking", true, "reply_stop")]
    [TestCase("bye all, safe travels", false, "reply_farewell")]
    public void ClassifyPlayerMessage_PicksTheReplySituation(string message, bool directed, string expected)
    {
        List<string> keys = BanterLibrary165.ClassifyPlayerMessage(message, directed);
        Assert.That(keys[0], Is.EqualTo(expected), string.Join(",", keys));
        Assert.That(keys.Last(), Is.EqualTo("reply_general"));
        Assert.That(keys.All(BanterLibrary165.Situations.ContainsKey), Is.True);
    }

    [Test]
    public void ClassifyNotice_ReadsTheRealHeraldWording()
    {
        Assert.That(BanterLibrary165.ClassifyNotice("raid-golestandt", RealmEventBanter.RaidReminder("Golestandt", false)), Is.EqualTo("raid_forming"));
        Assert.That(BanterLibrary165.ClassifyNotice("raid-golestandt", RealmEventBanter.RaidOutcome("Golestandt", "Failed rally")), Is.EqualTo("raid_ended"));
        Assert.That(BanterLibrary165.ClassifyNotice("raid-golestandt", RealmEventBanter.RaidOutcome("Golestandt", "Ended (unconfirmed)")), Is.EqualTo("raid_ended"));
        Assert.That(BanterLibrary165.ClassifyNotice("raid-golestandt", RealmEventBanter.RaidOutcome("Golestandt", "Timed out")), Is.EqualTo("raid_ended"));
        for (int i = 0; i < 10; i++)
            Assert.That(BanterLibrary165.ClassifyNotice("raid-golestandt", RealmEventBanter.RaidOutcome("Golestandt", "Boss defeated")), Is.EqualTo("raid_victory"));
        Assert.That(BanterLibrary165.ClassifyNotice("raid-golestandt", "The Golestandt expedition is advancing with 210 staged level-50 adventurers."), Is.EqualTo("raid_boss"));
        Assert.That(BanterLibrary165.ClassifyNotice("rvr-keep-5", RealmEventBanter.SiegeReminder("Caer Benowyc", true)), Is.EqualTo("rvr_defend"));
        Assert.That(BanterLibrary165.ClassifyNotice("rvr-keep-5", RealmEventBanter.SiegeReminder("Caer Benowyc", false)), Is.EqualTo("rvr_siege"));
        Assert.That(BanterLibrary165.ClassifyNotice("rvr-keep-5", RealmEventBanter.SiegeOutcome("Caer Benowyc", true, true, false, eRealm.None, eRealm.Albion)), Is.EqualTo("rvr_victory"));
        Assert.That(BanterLibrary165.ClassifyNotice("rvr-keep-5", RealmEventBanter.SiegeOutcome("Caer Benowyc", false, false, false, eRealm.Hibernia, eRealm.Midgard)), Is.EqualTo("rvr_defeat"));
        Assert.That(BanterLibrary165.ClassifyNotice("rvr-keep-5", "Caer Benowyc has fallen! Rally at the outpost: we take it back."), Is.EqualTo("rvr_siege"));
        Assert.That(BanterLibrary165.ClassifyNotice("rvr-relic-carrier-1-0", "The relic has left Dun Dagda! The battle continues on the road; its fate is not yet decided."), Is.EqualTo("rvr_relic"));
        Assert.That(BanterLibrary165.ClassifyNotice("rvr-keep-5", "The fighting for Caer Benowyc has ended. Await confirmed word before claiming victory."), Is.Null);
        // relic road outcomes (AutonomousRvrEventLayer.EndTarget): a relic carried off by another realm is a loss (owner 2026-11-10)
        Assert.That(BanterLibrary165.ClassifyNotice("rvr-relic-carrier-1-0", "Hibernia has secured Merlin's Staff at a shrine. Our struggle on the road is over."), Is.EqualTo("rvr_defeat"));
        Assert.That(BanterLibrary165.ClassifyNotice("rvr-relic-carrier-1-0", "Merlin's Staff is safe in our shrine! Honour to its bearers and defenders."), Is.EqualTo("rvr_victory"));
        Assert.That(BanterLibrary165.ClassifyNotice("rvr-relic-carrier-1-0", "Merlin's Staff has gone back to its shrine. The struggle on the road has ended."), Is.Null);
        Assert.That(BanterLibrary165.KeepIdOf("rvr-keep-42"), Is.EqualTo(42));
        Assert.That(BanterLibrary165.KeepIdOf("rvr-relic-carrier-1-0"), Is.EqualTo(-1));
    }

    [Test]
    public void SmallPureRules()
    {
        Assert.That(BanterLibrary165.WasResurrectedInPlace(1, 1000, 1000, 1, 1500, 1500), Is.True);
        Assert.That(BanterLibrary165.WasResurrectedInPlace(1, 1000, 1000, 1, 9000, 9000), Is.False);
        Assert.That(BanterLibrary165.WasResurrectedInPlace(1, 1000, 1000, 2, 1000, 1000), Is.False);
        Assert.That(BanterLibrary165.ClassTypesOf("Cleric"), Does.Contain("healer"));
        Assert.That(BanterLibrary165.ClassTypesOf("Bonedancer"), Does.Contain("pet"));
        Assert.That(BanterLibrary165.ClassTypesOf("Nobody"), Is.Empty);
        Assert.That(BanterLibrary165.EnemyRealmName(eRealm.Albion, eRealm.Midgard, new Random(1)), Is.EqualTo("Midgard"));
        for (int seed = 0; seed < 20; seed++)
            Assert.That(BanterLibrary165.EnemyRealmName(eRealm.Albion, eRealm.None, new Random(seed)), Is.AnyOf("Midgard", "Hibernia"));
        Assert.That(BanterLibrary165.LoadFolder(Path.Combine(Path.GetTempPath(), "no-such-banter165-folder")).Count, Is.EqualTo(0));
    }

    [Test]
    public void LevelUp_OnlyShortlyAfterTheLevelCame()
    {
        const long at = 1_000_000;
        Assert.That(BanterLibrary165.IsRecentLevelUp(0, at), Is.False);
        Assert.That(BanterLibrary165.IsRecentLevelUp(at, at + 9 * 60_000), Is.True);
        Assert.That(BanterLibrary165.IsRecentLevelUp(at, at + BanterLibrary165.LevelUpWindowMilliseconds), Is.False);
        Assert.That(BanterLibrary165.IsRecentLevelUp(at, at + 3 * 60 * 60_000), Is.False);
        Assert.That(BanterLibrary165.IsRecentLevelUp(at + 5_000, at), Is.False);
        int leads = 0;
        for (int seed = 0; seed < 400; seed++)
            if (BanterLibrary165.ResolveAmbient(new BanterLibrary165.Facts(LeveledUp: true), new Random(seed))[0] == "levelup")
                leads++;
        Assert.That(leads, Is.InRange(140, 260));
        Assert.That(BanterLibrary165.ResolveAmbient(new BanterLibrary165.Facts(), new Random(1)), Does.Not.Contain("levelup"));
    }

    [TestCase("ty", true)]
    [TestCase("rez pls", true)]
    [TestCase("ding 30", true)]
    [TestCase("anyone watch the epic football game", false)]
    [TestCase("bg tonight or the election", false)]
    [TestCase("hello there", false)]
    public void PeriodWordRescues_NeverOverAHardOutOfWorldWord(string message, bool expected)
    {
        Assert.That(BanterLibrary165.PeriodWordRescues(message), Is.EqualTo(expected));
    }

    [Test]
    public void EnemyRealm_ComesFromTheEventOrTheNotice()
    {
        Assert.That(BanterLibrary165.EnemyOf(eRealm.Albion, eRealm.Midgard, eRealm.Albion), Is.EqualTo(eRealm.Midgard));     // defender
        Assert.That(BanterLibrary165.EnemyOf(eRealm.Midgard, eRealm.Midgard, eRealm.Albion), Is.EqualTo(eRealm.Albion));     // attacker
        Assert.That(BanterLibrary165.EnemyOf(eRealm.Hibernia, eRealm.Midgard, eRealm.Albion), Is.EqualTo(eRealm.Albion));    // third realm: keep owner
        Assert.That(BanterLibrary165.EnemyOf(eRealm.Hibernia, eRealm.Midgard, eRealm.None), Is.EqualTo(eRealm.Midgard));
        Assert.That(BanterLibrary165.EnemyOf(eRealm.Albion, eRealm.None, eRealm.Albion), Is.EqualTo(eRealm.None));
        Assert.That(BanterLibrary165.EnemyOf(eRealm.Albion, eRealm.None, eRealm.None), Is.EqualTo(eRealm.None));
        Assert.That(BanterLibrary165.RealmNamedIn("Hibernia has secured Merlin's Staff at a shrine. Our struggle on the road is over.", eRealm.Albion), Is.EqualTo(eRealm.Hibernia));
        Assert.That(BanterLibrary165.RealmNamedIn(RealmEventBanter.SiegeOutcome("Caer Benowyc", true, false, false, eRealm.Midgard, eRealm.Albion), eRealm.Albion), Is.EqualTo(eRealm.Midgard));
        Assert.That(BanterLibrary165.RealmNamedIn("Albion holds the line.", eRealm.Albion), Is.EqualTo(eRealm.None));
        Assert.That(BanterLibrary165.RealmNamedIn("Albion and Midgard clash at the bridge.", eRealm.Hibernia), Is.EqualTo(eRealm.None));
        Assert.That(BanterLibrary165.RealmNamedIn(null, eRealm.Albion), Is.EqualTo(eRealm.None));
    }
}
