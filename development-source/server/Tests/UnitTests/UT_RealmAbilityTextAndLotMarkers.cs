using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DOL.GS.Housing;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// GitHub #35: realm ability delve text showed "EN Info1" because English RealmAbilities keys were empty.
/// GitHub #31: lot markers must not sell the consignment merchant deed (the Realm Exchange replaces it).
/// </summary>
[TestFixture]
public class UT_RealmAbilityTextAndLotMarkers
{
    private static readonly Regex TranslationKey = new(@"GetTranslation\([^,()]+,\s*""([A-Za-z0-9_.]+)""", RegexOptions.Compiled);

    // The repo root (holds GameServer\language and the realm ability sources).
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "GameServer", "language", "EN")) &&
                Directory.Exists(Path.Combine(dir.FullName, "GameServer", "realmabilities")))
                return dir.FullName;
        }
        return null;
    }

    // Same parsing as LanguageMgr.ReadLanguageDirectory: first occurrence of an id wins, tabs become spaces, trimmed.
    private static Dictionary<string, string> ReadEnglish(string root)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(Path.Combine(root, "GameServer", "language", "EN"), "*.txt", SearchOption.AllDirectories))
        {
            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (line.StartsWith('#') || !line.Contains(':'))
                    continue;
                string id = line[..line.IndexOf(':')];
                string text = line[(line.IndexOf(':') + 1)..].Replace("\t", " ").Trim();
                entries.TryAdd(id, text);
            }
        }
        return entries;
    }

    [Test]
    public void EveryEnglishRealmAbilityEntry_HasText()
    {
        string root = RepoRoot();
        Assert.That(root, Is.Not.Null, "repo root not found above the test directory");
        string file = Path.Combine(root, "GameServer", "language", "EN", "RealmAbilities.txt");

        var empty = File.ReadAllLines(file, Encoding.UTF8)
            .Where(l => !l.StartsWith('#') && l.Contains(':'))
            .Where(l => l[(l.IndexOf(':') + 1)..].Replace("\t", " ").Trim().Length == 0)
            .Select(l => l[..l.IndexOf(':')])
            .ToList();

        Assert.That(empty, Is.Empty, "empty English realm ability keys show as 'EN InfoN' in the delve window");
    }

    [Test]
    public void EveryTranslationKeyUsedByRealmAbilities_HasEnglishText()
    {
        string root = RepoRoot();
        Assert.That(root, Is.Not.Null, "repo root not found above the test directory");
        Dictionary<string, string> english = ReadEnglish(root);

        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string folder in new[] { "realmabilities", "realmabilities_atlasOF" })
        {
            string path = Path.Combine(root, "GameServer", folder);
            if (!Directory.Exists(path))
                continue;
            foreach (string source in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in TranslationKey.Matches(File.ReadAllText(source)))
                    used.Add(m.Groups[1].Value);
        }

        Assert.That(used, Is.Not.Empty);
        var missing = used.Where(k => !english.TryGetValue(k, out string text) || text.Length == 0).ToList();
        Assert.That(missing, Is.Empty, "realm ability translation keys with no English text");
    }

    [Test]
    public void LotMarkers_NeverSellTheConsignmentDeed()
    {
        Assert.That(HouseTemplateMgr.LotMarkerListNames, Has.Length.EqualTo(4));
        foreach (string list in HouseTemplateMgr.LotMarkerListNames)
        {
            string[] items = HouseTemplateMgr.GetLotMarkerItemIds(list);
            Assert.That(items, Does.Not.Contain("housing_consignment_deed"), list);
            Assert.That(items, Does.Contain("housing_porch_deed"), list);
            Assert.That(items.Count(i => i.EndsWith("_cottage_deed")), Is.EqualTo(list == "housing_custom_lotmarker" ? 3 : 1), list);
        }
        Assert.That(HouseTemplateMgr.GetLotMarkerItemIds("unknown"), Is.Empty);
    }
}
