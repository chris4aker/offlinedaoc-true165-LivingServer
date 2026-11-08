using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-08: melee healers (Shaman, Friar, Warden ...) turned between the monster and the groupmate over and
    /// over before a heal went off. A bot only turns for spells that need facing, and a cast-time heal waits out the bot's
    /// own swing instead of failing and turning back to the monster each try. Monsters and pets keep turning as before.
    /// </summary>
    [TestFixture]
    public class UT_MeleeHealerFacing
    {
        [Test]
        public void BotsTurnOnlyForSpellsThatNeedFacing()
        {
            Assert.That(SpellHandler.NpcTurnsForSpell(true, eSpellTarget.ENEMY), Is.True);
            Assert.That(SpellHandler.NpcTurnsForSpell(true, eSpellTarget.CONE), Is.True);
            Assert.That(SpellHandler.NpcTurnsForSpell(true, eSpellTarget.REALM), Is.False, "a heal on a groupmate");
            Assert.That(SpellHandler.NpcTurnsForSpell(true, eSpellTarget.GROUP), Is.False);
            Assert.That(SpellHandler.NpcTurnsForSpell(false, eSpellTarget.REALM), Is.True, "monsters and pets unchanged");
        }

        [Test]
        public void CastTimeHealWaitsOutTheBotsOwnSwing()
        {
            Assert.That(BotBrain.HealWaitsForSwing(false, false, true, false), Is.True, "just swung: wait, then cast once");
            Assert.That(BotBrain.HealWaitsForSwing(false, false, false, false), Is.False, "swing over: cast now");
            Assert.That(BotBrain.HealWaitsForSwing(true, false, true, false), Is.False, "instant heals never wait");
            Assert.That(BotBrain.HealWaitsForSwing(false, true, true, false), Is.False, "uninterruptible heals never wait");
            Assert.That(BotBrain.HealWaitsForSwing(false, false, true, true), Is.False, "being hit: the existing rules decide");
        }
    }
}
