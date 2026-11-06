using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public sealed class UT_AutonomousCampLevelPolicy
{
    [Test]
    public void PineImpsCannotAdvertiseAnAbsentLevelNine()
    {
        Assert.That(AutonomousCampLevelPolicy.ObservedAuthoredLevels([9], [16, 17, 18]),
            Is.EqualTo(new[] { 16, 17, 18 }));
    }

    [Test]
    public void HillPeopleCannotAdvertiseAnAbsentLevelTwelve()
    {
        Assert.That(AutonomousCampLevelPolicy.ObservedAuthoredLevels([12], [6, 7]),
            Is.EqualTo(new[] { 6, 7 }));
    }

    [Test]
    public void MatchingLiveLevelsRemainAvailableWithoutDuplicatingTargets()
    {
        Assert.That(AutonomousCampLevelPolicy.ObservedAuthoredLevels([10, 11, 12], [12, 10, 10, 13]),
            Is.EqualTo(new[] { 10, 12 }));
    }

    [Test]
    public void MissingInputsNeverCreateAPhantomCamp()
    {
        Assert.That(AutonomousCampLevelPolicy.ObservedAuthoredLevels(null, [10]), Is.Empty);
        Assert.That(AutonomousCampLevelPolicy.ObservedAuthoredLevels([10], null), Is.Empty);
    }

    [Test]
    public void SpawnLevelsCountEveryLiveMonster()
    {
        // skeletal pawns listed at 1-2, nearly all live ones level 1: a level-4 bot (1 grey, 2 green) must see 1 of 5
        int[] levels = AutonomousCampLevelPolicy.ObservedAuthoredSpawnLevels([1, 2], [1, 1, 2, 1, 1]);
        Assert.That(levels, Is.EqualTo(new[] { 1, 1, 1, 1, 2 }));
        Assert.That(AutonomousCampLevelPolicy.ObservedAuthoredSpawnLevels([12], [6, 7, 6]), Is.EqualTo(new[] { 6, 6, 7 }));
        Assert.That(AutonomousCampLevelPolicy.ObservedAuthoredSpawnLevels([10, 11, 12], [12, 10, 10, 13]), Is.EqualTo(new[] { 10, 10, 12 }));
        Assert.That(AutonomousCampLevelPolicy.ObservedAuthoredSpawnLevels(null, [10]), Is.Empty);
    }
}
