using System.Collections.Generic;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using Kind = DOL.GS.BotControlSpells.ControlKind;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-08: companions and gamebots use crowd control like a veteran player. Mez and root only on untouched
    /// adds, a snare only on a runner, a stun only to interrupt or peel, long-reuse control only in emergencies, and none
    /// of it in the ordinary nuke rotation. Animist bombers are classified by their payload.
    /// </summary>
    [TestFixture]
    public class UT_BotVeteranCrowdControl
    {
        private static Spell S(string type, double value = 0, double damage = 0, int recast = 0, int subSpell = 0,
            int radius = 0, int id = 0) =>
            new(new DbSpell { SpellID = id, Name = type, Type = type, Target = "Enemy", Value = value, Damage = damage,
                RecastDelay = recast, SubSpellID = subSpell, Radius = radius, Range = 1500 }, 10);

        [Test]
        public void ControlSpellsAreClassifiedByType() => Assert.Multiple(() =>
        {
            Assert.That(BotControlSpells.KindOfType(S("Mesmerize")), Is.EqualTo(Kind.Mez));
            Assert.That(BotControlSpells.KindOfType(S("Stun")), Is.EqualTo(Kind.Stun), "Halt, Stunning Flash, Prismatic Flare");
            Assert.That(BotControlSpells.KindOfType(S("Nearsight", 25)), Is.EqualTo(Kind.Nearsight), "Suppress Sight");
            Assert.That(BotControlSpells.KindOfType(S("SpeedDecrease", 40)), Is.EqualTo(Kind.Snare), "Shroud of Doubt, Slowness");
            Assert.That(BotControlSpells.KindOfType(S("SpeedDecrease", 99)), Is.EqualTo(Kind.Root), "Creepers");
            Assert.That(BotControlSpells.KindOfType(S("UnbreakableSpeedDecrease", 40)), Is.EqualTo(Kind.Snare));
            Assert.That(BotControlSpells.KindOfType(S("UnbreakableSpeedDecrease", 99)), Is.EqualTo(Kind.Root));
            Assert.That(BotControlSpells.KindOfType(S("DamageSpeedDecrease", 40, 148)), Is.EqualTo(Kind.None),
                "a snare-nuke is damage");
            Assert.That(BotControlSpells.KindOfType(S("DirectDamage", 0, 100)), Is.EqualTo(Kind.None));
            Assert.That(BotControlSpells.KindOfType(S("StrengthDebuff", 20)), Is.EqualTo(Kind.None));
            Assert.That(BotControlSpells.KindOfType(null), Is.EqualTo(Kind.None));
        });

        [Test]
        public void BombersAreClassifiedByTheirPayload()
        {
            var payloads = new Dictionary<int, Spell>
            {
                [11367] = S("SpeedDecrease", 99, id: 11367),          // Rampant Vines
                [11372] = S("SpeedDecrease", 99, radius: 350, id: 11372),   // Binding Spirit
                [11110] = S("DirectDamage", 0, 225, id: 11110),        // Spirit of Hatred
            };
            Spell Resolve(int id) => payloads.TryGetValue(id, out Spell spell) ? spell : null;
            Spell vines = S("Bomber", 50, 50, subSpell: 11367);
            Spell binding = S("Bomber", 50, 50, subSpell: 11372);
            Spell hatred = S("Bomber", 50, 50, subSpell: 11110);
            Spell unknown = S("Bomber", 50, 50, subSpell: 999999);
            Assert.Multiple(() =>
            {
                Assert.That(BotControlSpells.KindWith(vines, Resolve), Is.EqualTo(Kind.Root));
                Assert.That(BotControlSpells.IsControlBomberWith(binding, Resolve), Is.True);
                Assert.That(BotControlSpells.IsControlBomberWith(hatred, Resolve), Is.False);
                Assert.That(BotControlSpells.IsControlWith(unknown, Resolve), Is.False, "an unknown payload is not guessed");
                Assert.That(BotCasterPriority.IsDamageWith(vines, Resolve), Is.False, "a root bomber is no nuke");
                Assert.That(BotCasterPriority.IsDamageWith(binding, Resolve), Is.False);
                Assert.That(BotCasterPriority.IsDamageWith(hatred, Resolve), Is.True);
                Assert.That(BotCasterPriority.IsDamageWith(unknown, Resolve), Is.True, "unchanged when unresolved");
            });
        }

        [Test]
        public void ControlIsNoDebuffAndSnareNukesAreDamage() => Assert.Multiple(() =>
        {
            foreach (Spell control in new[] { S("Stun"), S("Nearsight", 25), S("SpeedDecrease", 40), S("SpeedDecrease", 99),
                         S("UnbreakableSpeedDecrease", 40), S("Mesmerize") })
                Assert.That(BotCasterPriority.IsDebuff(control), Is.False, control.Name);
            Assert.That(BotCasterPriority.IsDamage(S("DamageSpeedDecrease", 40, 148)), Is.True);
            Assert.That(ShamanBotCombatPolicy.IsRoot(S("UnbreakableSpeedDecrease", 99)), Is.True);
            Assert.That(ShamanBotCombatPolicy.IsRoot(S("UnbreakableSpeedDecrease", 40)), Is.False);
        });

        [Test]
        public void OnTheBotsOwnMonsterOnlyRunnerSnaresAndPurposefulStunsGo() => Assert.Multiple(() =>
        {
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.None, false, false, false), Is.True, "nukes are not control");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Mez, false, true, true), Is.False);
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Root, false, true, true), Is.False, "Druid roots its own target");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Nearsight, false, true, true), Is.False, "Runemaster on monsters");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Snare, false, false, true), Is.False, "a held monster");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Snare, false, true, false), Is.True, "a runner");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Stun, false, true, false), Is.False, "stun as filler");
            Assert.That(BotControlSpells.AllowsOnOwnTarget(Kind.Stun, false, false, true), Is.True);
            foreach (Kind kind in new[] { Kind.Mez, Kind.Root, Kind.Snare, Kind.Stun, Kind.Nearsight })
                Assert.That(BotControlSpells.AllowsOnOwnTarget(kind, true, false, false), Is.True, "PvP keeps " + kind);
        });

        [TestCase(true, 20, false, true)]
        [TestCase(true, 20, true, false)]    // still swinging at its victim
        [TestCase(false, 20, false, false)]  // standing
        [TestCase(true, 80, false, false)]   // healthy and walking: no runner
        [TestCase(true, 35, false, false)]
        public void ARunnerMovesIsLowAndIsOutOfMelee(bool moving, int health, bool inMelee, bool runner) =>
            Assert.That(BotControlSpells.IsRunner(moving, health, inMelee), Is.EqualTo(runner));

        [TestCase(true, false, false, false, true)]   // interrupt a casting monster
        [TestCase(false, true, false, false, true)]   // peel a monster off the healer
        [TestCase(false, false, false, true, false)]  // never filler, even in an emergency
        [TestCase(true, false, true, false, false)]   // Paralyzing Glance waits for an emergency
        [TestCase(false, true, true, true, true)]
        public void AStunInterruptsOrPeels(bool casting, bool peel, bool longReuse, bool emergency, bool worthwhile) =>
            Assert.That(BotControlSpells.StunWorthwhile(casting, peel, longReuse, emergency), Is.EqualTo(worthwhile));

        [Test]
        public void LongReuseIsAMinuteOrMore() => Assert.Multiple(() =>
        {
            Assert.That(BotControlSpells.IsLongReuse(S("Stun", recast: 600)), Is.True, "Paralyzing Glance");
            Assert.That(BotControlSpells.IsLongReuse(S("Mesmerize", recast: 600)), Is.True, "Hypnotic Harmony");
            Assert.That(BotControlSpells.IsLongReuse(S("Stun", recast: 10)), Is.False, "Stunning Yell");
            Assert.That(BotControlSpells.IsLongReuse(S("Stun")), Is.False, "Halt");
        });

        [Test]
        public void OnlyUntouchedAddsAreControlled() => Assert.Multiple(() =>
        {
            Assert.That(BotControlSpells.IsUntouchedAdd(false, false, false, false), Is.True);
            Assert.That(BotControlSpells.IsUntouchedAdd(true, false, false, false), Is.False, "somebody hits it");
            Assert.That(BotControlSpells.IsUntouchedAdd(false, true, false, false), Is.False, "a DoT ticks on it");
            Assert.That(BotControlSpells.IsUntouchedAdd(false, false, true, false), Is.False, "the bot's own target");
            Assert.That(BotControlSpells.IsUntouchedAdd(false, false, false, true), Is.False, "an elemental is on it");
        });

        [Test]
        public void ADamageShieldOnTheVictimIsNoAttacker()
        {
            object healer = new(), tank = new();
            Assert.Multiple(() =>
            {
                Assert.That(BotControlSpells.HasRealAttacker(new[] { healer }, healer, false), Is.False,
                    "the add meleeing a damage-shielded healer is hit back by the shield only");
                Assert.That(BotControlSpells.HasRealAttacker(new[] { healer }, healer, true), Is.True,
                    "the healer is swinging at it");
                Assert.That(BotControlSpells.HasRealAttacker(new[] { healer, tank }, healer, false), Is.True,
                    "the tank hits it");
                Assert.That(BotControlSpells.HasRealAttacker(new object[0], healer, false), Is.False);
                Assert.That(BotControlSpells.HasRealAttacker<object>(null, healer, false), Is.False);
            });
        }

        [Test]
        public void HoldingDamageKeepsDebuffsAndPurposefulStunsAndPvpControl()
        {
            Spell stun = S("Stun"), snare = S("SpeedDecrease", 40), root = S("SpeedDecrease", 99), mez = S("Mesmerize"),
                nearsight = S("Nearsight", 25), debuff = S("StrengthDebuff", 20), nuke = S("DirectDamage", 0, 100),
                snareNuke = S("DamageSpeedDecrease", 40, 148);
            Assert.Multiple(() =>
            {
                Assert.That(BotControlSpells.HoldDamageStrips(debuff, false), Is.False);
                Assert.That(BotControlSpells.HoldDamageStrips(stun, false), Is.False, "an approved stun interrupts or peels");
                Assert.That(BotControlSpells.HoldDamageStrips(nuke, false), Is.True);
                Assert.That(BotControlSpells.HoldDamageStrips(snareNuke, false), Is.True);
                foreach (Spell control in new[] { snare, root, mez, nearsight })
                {
                    Assert.That(BotControlSpells.HoldDamageStrips(control, false), Is.True, control.Name + " on a monster");
                    Assert.That(BotControlSpells.HoldDamageStrips(control, true), Is.False, control.Name + " on a player's pet");
                }
                Assert.That(BotControlSpells.HoldDamageStrips(nuke, true), Is.True);
            });
        }

        [Test]
        public void AHeldTargetGetsThePlainNukeOverASnareNuke()
        {
            Spell demobilizing = S("DamageSpeedDecrease", 40, 148);
            Spell plain = S("DirectDamage", 0, 140);
            Spell weak = S("DirectDamage", 0, 60);
            Spell area = S("DirectDamage", 0, 160, radius: 350);
            Assert.Multiple(() =>
            {
                Assert.That(BotControlSpells.PrefersPlainNuke(demobilizing, plain, true), Is.True);
                Assert.That(BotControlSpells.PrefersPlainNuke(demobilizing, plain, false), Is.False, "nobody holds it");
                Assert.That(BotControlSpells.PrefersPlainNuke(demobilizing, weak, true), Is.False, "no comparable nuke");
                Assert.That(BotControlSpells.PrefersPlainNuke(demobilizing, area, true), Is.False, "an area spell is another job");
                Assert.That(BotControlSpells.PrefersPlainNuke(plain, demobilizing, true), Is.False);
            });
        }
    }
}
