using System.Text.Json;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// classic-quests.json is written by the goal 10 generator (development-source/research/period-data/quests); these
/// samples are copied from its output so a renamed property cannot silently switch a step off.
/// </summary>
[TestFixture]
public class UT_ClassicQuestsConfig
{
    private const string Sample = """
    {
      "Quests": {
        "20001": { "Steps": [ null,
          { "Marker": { "Region": 1, "X": 529929, "Y": 479033, "Z": 2200 },
            "Custom": { "Kind": "trade", "Target": "Stonemason Harwin",
                        "Trades": { "cq_flat_chert": "cq_flint_knife", "cq_round_chert": "" },
                        "Drops": [ { "Mob": "river spriteling", "Item": "cq_flat_chert", "Chance": 30 } ] } },
          { "Grant": [ "cq_vial_of_mud" ], "GrantFrom": "Lirele" },
          { "Custom": { "Kind": "use_item", "Item": "cq_wounding_stone", "Consume": true, "Give": true,
                        "Region": 1, "X": 43000, "Y": 45000, "Z": 0, "Radius": 600 } },
          { "Custom": { "Kind": "quest", "Quest": "Wizard Lost|Barbaric Tales|Forged Excellence", "Count": 2 } },
          { "Custom": { "Kind": "choose", "Target": "Master Vismer",
                        "Choices": { "Staff of Frozen Tears": "cq_alb_staff_of_frozen_tears" } } } ],
          "Races": [ "Briton" ] }
      },
      "QuestMonsterIds": [ "abc" ],
      "Chat": { "Troya": { "have time": "Excellent!" } }
    }
    """;

    [Test]
    public void GeneratorOutput_ParsesIntoEveryStepField()
    {
        var config = JsonSerializer.Deserialize<ClassicQuests.Config>(Sample, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var steps = config.Quests[20001].Steps;
        Assert.Multiple(() =>
        {
            Assert.That(steps[0], Is.Null);
            Assert.That(steps[1].Custom.Kind, Is.EqualTo("trade"));
            Assert.That(steps[1].Custom.Trades["cq_flat_chert"], Is.EqualTo("cq_flint_knife"));
            Assert.That(steps[1].Custom.Trades["cq_round_chert"], Is.Empty);
            Assert.That(steps[1].Custom.Drops[0].Mob, Is.EqualTo("river spriteling"));
            Assert.That(steps[1].Custom.Drops[0].Chance, Is.EqualTo(30));
            Assert.That(steps[2].Grant, Is.EqualTo(new[] { "cq_vial_of_mud" }));
            Assert.That(steps[2].GrantFrom, Is.EqualTo("Lirele"));
            Assert.That(steps[3].Custom.Consume && steps[3].Custom.Give, Is.True);
            Assert.That(steps[3].Custom.Radius, Is.EqualTo(600));
            Assert.That(steps[4].Custom.Count, Is.EqualTo(2));
            Assert.That(steps[5].Custom.Choices["Staff of Frozen Tears"], Is.EqualTo("cq_alb_staff_of_frozen_tears"));
            Assert.That(config.Chat["Troya"]["have time"], Is.EqualTo("Excellent!"));
        });
    }

    [Test]
    public void CustomStep_DefaultsToAModestRadius()
    {
        Assert.That(new ClassicQuests.CustomStep().Radius, Is.EqualTo(600));
    }

    // Owner 2026-11-09: a fight spawn is skipped only when a monster of that name the player can fight already stands
    // there. The quest's own peaceful NPC (Fressen, Aricoer, Ciar) or a guard of the player's realm is not that monster.
    [Test]
    public void OnlyAnNpcThePlayerCanFightStandsInForAFightSpawn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClassicQuests.CountsAsFight(0, eRealm.None, eRealm.Midgard), Is.True, "a hostile monster");
            Assert.That(ClassicQuests.CountsAsFight(GameNPC.eFlags.GHOST, eRealm.None, eRealm.Albion), Is.True, "a hostile ghost");
            Assert.That(ClassicQuests.CountsAsFight(GameNPC.eFlags.PEACE, eRealm.Midgard, eRealm.Midgard), Is.False, "the quest's peaceful NPC");
            Assert.That(ClassicQuests.CountsAsFight(GameNPC.eFlags.PEACE | GameNPC.eFlags.GHOST, eRealm.None, eRealm.Albion), Is.False, "a peaceful ghost");
            Assert.That(ClassicQuests.CountsAsFight(0, eRealm.Albion, eRealm.Albion), Is.False, "a guard of the player's realm");
        });
    }
}
