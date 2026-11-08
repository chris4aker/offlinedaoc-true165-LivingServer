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

        private static Spell Choose(bool held, bool debuffOn, params Spell[] ready) =>
            BotBrain.ChooseServantCommand(ready, id => Payloads.TryGetValue(id, out Spell payload) ? payload : null, held, debuffOn);

        [Test]
        public void HeldMonster_OneDebuffNoSnare_ThenThePowerDrain()
        {
            Spell[] all = [Snare, ArmorDebuff, ConDebuff, LifeDrain, PowerDrain, Cackle];
            Assert.That(Choose(true, false, all), Is.SameAs(ArmorDebuff));
            Assert.That(Choose(true, false, Snare, LifeDrain, PowerDrain, Cackle), Is.SameAs(PowerDrain), "no snare on a held monster");
            Assert.That(Choose(true, true, Snare, ConDebuff, LifeDrain, PowerDrain, Cackle), Is.SameAs(PowerDrain), "one debuff is enough");
        }

        [Test]
        public void Damage_BestCommandNeverRandom()
        {
            for (int i = 0; i < 20; i++)
                Assert.That(Choose(true, true, LifeDrain, Cackle), Is.SameAs(LifeDrain), "a level 1 command never beats a better one");
        }

        [Test]
        public void LooseMonster_DebuffsFirstSnareIncluded()
        {
            Assert.That(Choose(false, true, Snare, LifeDrain, PowerDrain), Is.SameAs(Snare));
            Assert.That(Choose(false, false, Snare, ArmorDebuff, PowerDrain), Is.SameAs(ArmorDebuff), "the highest debuff first");
        }

        [Test]
        public void HeldMonster_OnlyDebuffsLeft_StillCasts()
        {
            Assert.That(Choose(true, true, ConDebuff), Is.SameAs(ConDebuff));
            Assert.That(Choose(true, true, Snare), Is.Null);
        }
    }
}
