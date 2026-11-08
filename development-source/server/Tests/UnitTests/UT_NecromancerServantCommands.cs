using System.Collections.Generic;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner report 2026-10-15: Necromancer bots did not use the servant's area debuffs on a single target and sent
    /// Billowing Death (a burst around the servant) from far away. Area debuffs are now picked first when the target lacks
    /// them; the burst needs the servant beside the target.
    /// </summary>
    [TestFixture]
    public class UT_NecromancerServantCommands
    {
        private static Spell Payload(string type, int range, int radius) =>
            new(new DbSpell { Type = type, Target = "Enemy", Range = range, Radius = radius, Duration = 60 }, 40);

        [Test]
        public void AreaDebuffsCountAsDebuffs()
        {
            Assert.That(BotBrain.IsServantDebuff(Payload("StrengthConstitutionDebuff", 1500, 350)), Is.True);  // Shroud of Affliction
            Assert.That(BotBrain.IsServantDebuff(Payload("SpeedDecrease", 1500, 350)), Is.True);               // Vortex of Anguish
            Assert.That(BotBrain.IsServantDebuff(Payload("DamageOverTime", 0, 350)), Is.False);               // Billowing Death
            Assert.That(BotBrain.IsServantDebuff(Payload("DirectDamage", 0, 350)), Is.False);                 // Channeled Anger
        }

        // Aldith's level 15 Deathsight book (owner 2026-11-08): wrapper level, payload type and damage.
        private static readonly Dictionary<int, Spell> Payloads = new();

        private static Spell Command(int id, int level, string type, double damage)
        {
            Payloads[id] = new Spell(new DbSpell { SpellID = id, Type = type, Target = "Enemy", Range = 1500, Damage = damage, Duration = 30 }, level);
            return new Spell(new DbSpell { SpellID = id + 1000, Type = "PetSpell", Target = "Enemy", SubSpellID = id }, level);
        }

        private static readonly Spell Snare = Command(1, 12, "SpeedDecrease", 0);          // Constricting Pain
        private static readonly Spell ArmorDebuff = Command(2, 14, "ArmorFactorDebuff", 0); // Glimpse of Death
        private static readonly Spell ConDebuff = Command(3, 11, "ConstitutionDebuff", 0);  // Intensify Pain
        private static readonly Spell LifeDrain = Command(4, 13, "Lifedrain", 49);          // Soul Evacuation
        private static readonly Spell PowerDrain = Command(5, 12, "PowerDrainPet", 45);     // Puissance Vacuum
        private static readonly Spell Cackle = Command(6, 1, "DirectDamage", 4);            // Tormenting Cackle

        private static Spell Choose(bool othersMelee, bool debuffOn, bool runner, params Spell[] ready) =>
            BotBrain.ChooseServantCommand(ready, id => Payloads.TryGetValue(id, out Spell payload) ? payload : null, othersMelee, debuffOn, runner);

        [Test]
        public void GroupFight_OneDebuffNoSnare_ThenThePowerDrain()
        {
            Spell[] all = [Snare, ArmorDebuff, ConDebuff, LifeDrain, PowerDrain, Cackle];
            Assert.That(Choose(true, false, false, all), Is.SameAs(ArmorDebuff), "a groupmate melees: the highest debuff once");
            Assert.That(Choose(true, true, false, all), Is.SameAs(PowerDrain), "one debuff is enough, then the power drain");
            Assert.That(Choose(true, false, false, Snare, LifeDrain, PowerDrain, Cackle), Is.SameAs(PowerDrain), "no snare on a held monster");
        }

        [Test]
        public void Solo_NoDebuffs_StraightToTheDrains()
        {
            Assert.That(Choose(false, false, false, Snare, ArmorDebuff, ConDebuff, LifeDrain, PowerDrain), Is.SameAs(PowerDrain));
            Assert.That(Choose(false, false, false, Snare, ArmorDebuff), Is.Null, "solo debuffs waste power");
        }

        [Test]
        public void Damage_BestCommandNeverRandom()
        {
            for (int i = 0; i < 20; i++)
                Assert.That(Choose(true, true, false, LifeDrain, Cackle), Is.SameAs(LifeDrain), "a level 1 command never beats a better one");
        }

        [Test]
        public void Runner_GetsTheSnare()
        {
            Assert.That(Choose(false, false, true, Snare, LifeDrain, PowerDrain), Is.SameAs(Snare));
            Assert.That(Choose(true, true, true, LifeDrain, PowerDrain), Is.SameAs(PowerDrain), "no snare learned: damage");
        }

        [Test]
        public void GroupFight_OnlyDebuffsLeft_StillCasts()
        {
            Assert.That(Choose(true, true, false, ConDebuff), Is.SameAs(ConDebuff));
            Assert.That(Choose(true, true, false, Snare), Is.Null);
        }

        [Test]
        public void DeathsightServant_HoldsBackUntilHit()
        {
            Assert.That(AutonomousPetSupport.IsDeathsightSpec(17, 1, 1), Is.True);       // Aldith
            Assert.That(AutonomousPetSupport.IsDeathsightSpec(1, 1, 1), Is.False);       // untrained: the servant goes in
            Assert.That(AutonomousPetSupport.IsDeathsightSpec(20, 30, 1), Is.False);     // Painworking
            Assert.That(AutonomousPetSupport.IsDeathsightSpec(20, 5, 24), Is.False);     // Death Servant
            Assert.That(AutonomousPetSupport.ServantHoldsBack(true, 60_000), Is.True);
            Assert.That(AutonomousPetSupport.ServantHoldsBack(true, 2_000), Is.False, "hit 2 s ago: it fights back");
            Assert.That(AutonomousPetSupport.ServantHoldsBack(false, 60_000), Is.False);
        }
    }
}
