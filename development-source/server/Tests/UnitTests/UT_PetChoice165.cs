using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>True 1.65 pet choices of pet casters without a player choice (period forums).</summary>
    [TestFixture]
    public sealed class UT_PetChoice165
    {
        private static (Spell Spell, SpellLine Line) Summon(string name, int level, string type = "SummonSimulacrum") =>
            (new Spell(new DbSpell { Name = name, Type = type, Target = "Self" }, level), null);

        [Test]
        public void ACabalistCallsSapphireOnceLearnedElseAmber() => Assert.Multiple(() =>
        {
            var amber = Summon("Amber Simulacrum", 1);
            var ruby = Summon("Ruby Simulacrum", 7);
            var sapphire = Summon("Sapphire Simulacrum", 12);
            var jade = Summon("Jade Simulacrum", 32);
            Assert.That(AutonomousPetSupport.CabalistPeriodPet([amber, ruby, sapphire, jade]).Spell, Is.SameAs(sapphire.Spell),
                "Sapphire: the best PvE pet, not the top rank Jade");
            Assert.That(AutonomousPetSupport.CabalistPeriodPet([amber, ruby]).Spell, Is.SameAs(amber.Spell),
                "before Sapphire: Amber, not Ruby");
            Assert.That(AutonomousPetSupport.CabalistPeriodPet([ruby]).Spell, Is.Null, "neither known: the default choice");
            Assert.That(AutonomousPetSupport.CabalistPeriodPet([Summon("Sapphire Slash", 44, "DirectDamage")]).Spell, Is.Null,
                "only a simulacrum summon counts");
        });

        [Test]
        public void AGroupedTheurgistKeepsPetsForEmergencies() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousPetSupport.TheurgistMaySummon(false, false), Is.True, "solo: pets are its tank");
            Assert.That(AutonomousPetSupport.TheurgistMaySummon(true, false), Is.False, "the tank holds everything: nuke, root");
            Assert.That(AutonomousPetSupport.TheurgistMaySummon(true, true), Is.True, "a monster on the caster or loose");
        });

        [Test]
        public void EarthElementalsAreTheEarthLine() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousPetSupport.IsEarthSummon(Summon("Call Earthlord", 48, "SummonTheurgistPet")), Is.True);
            Assert.That(AutonomousPetSupport.IsEarthSummon(Summon("Summon Ice Elemental", 32, "SummonTheurgistPet")), Is.False);
            Assert.That(AutonomousPetSupport.IsEarthSummon(Summon("Call Air Sprite", 12, "SummonTheurgistPet")), Is.False);
        });
    }
}
