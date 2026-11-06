using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// True 1.65: a bot casts every spell through the generic "Mob Spells" line, which is no specialization (0). Whatever
    /// scales with the caster's spec (buff effectiveness, spell damage item bonuses, charm, damage variance) takes the spec of
    /// the bot's learned line instead, as a player's own line (owner 2026-11-06). Monsters and normal mode are unchanged.
    /// </summary>
    [TestFixture]
    public sealed class UT_BotSpellLineSpec165
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class SpecBot : GameBot
        {
            private SpecBot() : base((OfflineWorldBotRecord)null) { }
            public int Spec;
            public override int GetModifiedSpecLevel(string keyName) => keyName == "Augmentation" ? Spec : 0;
            public override int GetModified(eProperty property) => 0;
        }

        private static readonly SpellLine Mob =
            new(GlobalSpellsLines.Mob_Spells, GlobalSpellsLines.Mob_Spells, GlobalSpellsLines.Mob_Spells, true);
        private static readonly SpellLine Augmentation = new("Shaman Augmentation Spec", "Augmentation", "Augmentation", false);

        private bool _mode;
        private IDisposable _server;

        [SetUp]
        public void Setup()
        {
            _mode = DOL.GS.ServerProperties.Properties.CLASSIC165_MODE;
            _server = new DOL.UnitTests.EpicTestServerScope();   // GameObject's quest cache needs a database
        }

        [TearDown]
        public void Cleanup()
        {
            DOL.GS.ServerProperties.Properties.CLASSIC165_MODE = _mode;
            _server.Dispose();
        }

        private static SpecBot Bot(int spec, params Spell[] learned)
        {
            var bot = (SpecBot)RuntimeHelpers.GetUninitializedObject(typeof(SpecBot));
            bot.Spec = spec;
            var lines = new Dictionary<int, SpellLine>();
            foreach (Spell spell in learned)
                lines[spell.ID] = Augmentation;
            typeof(GameBot).GetField("_powerSpellLines", Hidden).SetValue(bot, lines);
            return bot;
        }

        private static double BuffEffectiveness(SpellHandler handler) =>
            (double)typeof(SingleStatBuff).GetMethod("CalculateBuffDebuffEffectiveness", Hidden).Invoke(handler, null);

        [Test]
        public void ABotsStatBuffScalesWithItsLearnedLinesSpec()
        {
            DOL.GS.ServerProperties.Properties.CLASSIC165_MODE = true;
            Spell buff = new(new DbSpell { SpellID = 99961, Name = "Spec buff", Type = "StrengthBuff", Target = "Realm",
                Value = 20, Duration = 1200 }, 20);
            // StrengthBuff scales with spec (BuffReceivesSpecBonus): 0.75 + (spec - 1) x 0.5 / level, 0.75 to 1.25
            Assert.Multiple(() =>
            {
                Assert.That(BuffEffectiveness(new StrengthBuff(Bot(20, buff), buff, Mob)), Is.EqualTo(0.75 + 19 * 0.5 / 20).Within(1e-9),
                    "Augmentation 20 on a level 20 buff, as a player's");
                Assert.That(BuffEffectiveness(new StrengthBuff(Bot(1, buff), buff, Mob)), Is.EqualTo(0.75).Within(1e-9));
                Assert.That(BuffEffectiveness(new StrengthBuff(Bot(20), buff, Mob)), Is.EqualTo(0.75).Within(1e-9),
                    "no learned line: the Mob line's (no) spec, as before");
            });
        }
    }
}
