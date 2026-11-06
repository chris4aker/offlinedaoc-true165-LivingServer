using NUnit.Framework;
using static DOL.GS.BotCasterPriority;

namespace DOL.GS.Tests
{
    /// <summary>True 1.65: a grouped caster stops optional nukes under half power while the tank holds the monster.</summary>
    [TestFixture]
    public sealed class UT_CasterPowerSaving165
    {
        [Test]
        public void UnderHalfPowerTheTankDoesTheWork() => Assert.Multiple(() =>
        {
            Assert.That(SavePowerInGroup(40, 80, true), Is.True);
            Assert.That(SavePowerInGroup(SavePowerBelowPercent, 80, true), Is.False, "half power or more: nuke");
            Assert.That(SavePowerInGroup(40, 80, false), Is.False, "nobody holds it: the caster fights");
            Assert.That(SavePowerInGroup(40, FinishTargetBelowPercent - 1, true), Is.False, "finish a nearly dead monster");
        });
    }
}
