using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Item 48: a True 1.65 warband's main assist calls one target in period kill order and the members assist it.</summary>
    [TestFixture, NonParallelizable]
    public class UT_RvrWarbandAssist
    {
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => EmptyDatabase; }
        private GameServer _previous;
        [SetUp] public void SetUp()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }
        [TearDown] public void TearDown() => GameServer.LoadTestDouble(_previous);

        private sealed record Enemy(string Name, eCharacterClass? Class, double Distance, bool HitsOperator = false, bool Carrier = false);

        private static string Pick(params Enemy[] enemies) =>
            AutonomousRvrAssist.Pick(enemies, e => e.HitsOperator, e => e.Carrier,
                e => AutonomousRvrAssist.KillOrderRank(e.Class), e => e.Distance)?.Name;

        [Test]
        public void KillOrderIsHealersThenClothThenArchersThenTanks()
        {
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Cleric), Is.EqualTo(0));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Druid), Is.EqualTo(0));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Shaman), Is.EqualTo(0));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Bard), Is.EqualTo(0));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Skald), Is.EqualTo(0));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Minstrel), Is.EqualTo(0));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Wizard), Is.EqualTo(1));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Runemaster), Is.EqualTo(1));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Eldritch), Is.EqualTo(1));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Scout), Is.EqualTo(2));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Hunter), Is.EqualTo(2));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Ranger), Is.EqualTo(2));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Armsman), Is.EqualTo(3));
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Paladin), Is.EqualTo(3), "a paladin is a tank first");
            Assert.That(AutonomousRvrAssist.KillOrderRank(eCharacterClass.Infiltrator), Is.EqualTo(3));
            Assert.That(AutonomousRvrAssist.KillOrderRank(null), Is.EqualTo(4), "pets and engines last");
        }

        [Test]
        public void MainAssistPicksByKillOrderThenDistance() =>
            Assert.That(Pick(
                    new("close warrior", eCharacterClass.Warrior, 200),
                    new("archer", eCharacterClass.Hunter, 900),
                    new("far healer", eCharacterClass.Healer, 2_400),
                    new("near healer", eCharacterClass.Healer, 1_800),
                    new("wizard", eCharacterClass.Wizard, 700)),
                Is.EqualTo("near healer"));

        [Test]
        public void SiegeOperatorThreatStaysFirstThenTheRelicCarrier()
        {
            Assert.That(Pick(
                    new("healer", eCharacterClass.Cleric, 300),
                    new("carrier", eCharacterClass.Armsman, 900, Carrier: true),
                    new("hitting our ram crew", eCharacterClass.Berserker, 1_500, HitsOperator: true)),
                Is.EqualTo("hitting our ram crew"));
            Assert.That(Pick(
                    new("healer", eCharacterClass.Cleric, 300),
                    new("carrier", eCharacterClass.Armsman, 900, Carrier: true)),
                Is.EqualTo("carrier"));
        }

        [Test]
        public void NoCandidatesNoPick() => Assert.That(Pick(), Is.Null);

        [Test]
        public void AssistOnlyForTrue165RoamingWarbands()
        {
            Assert.That(AutonomousRvrAssist.Applies(true, true, false, false, false), Is.True);
            Assert.That(AutonomousRvrAssist.Applies(false, true, false, false, false), Is.False, "normal mode unchanged");
            Assert.That(AutonomousRvrAssist.Applies(true, false, false, false, false), Is.False, "solo roamers scan for themselves");
            Assert.That(AutonomousRvrAssist.Applies(true, true, true, false, false), Is.False, "keep defenders keep their own rules");
            Assert.That(AutonomousRvrAssist.Applies(true, true, false, true, false), Is.False, "siege fighters keep their own rules");
            Assert.That(AutonomousRvrAssist.Applies(true, true, false, false, true), Is.False, "a siege crew holds its engine");
        }

        private sealed class Target : GameNPC
        {
            public bool Dead;   // field initializers do not run on an uninitialized object
            public override bool IsAlive => !Dead;
        }

        private static Target NewTarget()
        {
            var target = (Target)RuntimeHelpers.GetUninitializedObject(typeof(Target));
            target.ObjectState = GameObject.eObjectState.Active;
            return target;
        }

        [Test]
        public void ACallLastsUntilItExpiresOrTheTargetDies()
        {
            object group = new();
            Target target = NewTarget();
            AutonomousRvrAssist.CallTarget(group, target, 10_000);
            Assert.That(AutonomousRvrAssist.CalledTarget(group, 10_000), Is.SameAs(target));
            Assert.That(AutonomousRvrAssist.CalledTarget(new object(), 10_000), Is.Null, "calls belong to one warband");
            Assert.That(AutonomousRvrAssist.CalledTarget(group, 10_000 + AutonomousRvrAssist.CallMilliseconds), Is.Null);
            AutonomousRvrAssist.CallTarget(group, target, 20_000);
            target.Dead = true;
            Assert.That(AutonomousRvrAssist.CalledTarget(group, 20_001), Is.Null);
        }
    }
}
