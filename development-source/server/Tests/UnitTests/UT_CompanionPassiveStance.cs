using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.Commands;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerClass;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>/passive companion stance (owner 2026-11-11: "for retreating or walking through areas without stopping to fight anything").</summary>
    [TestFixture, NonParallelizable]
    public class UT_CompanionPassiveStance
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previous;
        private readonly List<GameLiving> _actors = new();
        private sealed class Rules : NormalServerRules
        {
            public override bool IsAllowedToAttack(GameLiving attacker, GameLiving defender, bool quiet) =>
                attacker.Realm != defender.Realm && !BotPvpCrowdControl.Protected(attacker, defender);
        }
        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
            protected override IServerRules ServerRulesImpl => new Rules();
        }
        private sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public override byte Level { get => 50; set { } }
            public override bool IsAlive => true;
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override GameObject TargetObject { get; set; }
            public override IControlledBrain ControlledBrain { get; set; }
            public override IPacketLib Out => new BotDummyPacketLib();
        }
        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public ICharacterClass TestClass;
            public int TestX;
            public byte TestLevel = 1;
            public bool Alive = true;
            public bool Attacking;
            public eRealm TestRealm = eRealm.Albion;
            public override bool IsAlive => Alive;
            public override bool IsAttacking => Attacking;
            public override bool IsCasting => false;
            public override bool IsMoving => false;
            public override bool IsCrowdControlled => false;
            public override bool InCombat => false;
            public override short MaxSpeed => 200;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => TestRealm; set => TestRealm = value; }
            public override byte Level { get => TestLevel; set => TestLevel = value; }
            public override int EffectiveLevel => TestLevel;
            public override ICharacterClass CharacterClass => TestClass;
            public override IControlledBrain ControlledBrain { get; set; }
            public override void WalkTo(Vector3 target, short speed) { }
            public override void StopAttack() { Attacking = false; }
            public override void StartAttack(GameObject target) { }
            public override void Follow(GameObject target, int minDistance, int maxDistance) { }
        }
        private sealed class Enemy : GameNPC
        {
            public int TestX;
            public override bool IsAlive => true;
            public override bool IsCasting => false;
            public override bool IsAttacking => false;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Midgard; set { } }
            public override GameObject TargetObject { get; set; }
            public override byte Level { get => 1; set { } }
            public override int EffectiveLevel => 1;
            public override void Follow(GameObject target, int minDistance, int maxDistance) { }
            public override void StopAttack() { }
        }
        private sealed class Brain : BotBrain
        {
            public int Pulls;
            public override void AttackMostWanted() { }
            public override bool OrderPull(GameLiving target) { Pulls++; return true; }
        }
        private sealed class PetBrain : ControlledMobBrain
        {
            public int Disengages;
            public PetBrain(GameLiving owner) : base(owner) { }
            public override void Follow(GameObject target) { }
            public override void Disengage() { Disengages++; OrderedAttackTarget = null; }
        }
        [SetUp] public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }
        [TearDown] public void Cleanup()
        {
            foreach (GameLiving actor in _actors) ServiceObjectStore.Remove(actor.effectListComponent);
            _actors.Clear();
            GameServer.LoadTestDouble(_previous);
        }

        [Test] public void StanceRoundTripsAndOp30SendsZeroOneTwo()
        {
            Assert.That(CompanionEngagementMode.StanceByte(CompanionStance.Aggressive), Is.EqualTo(0));
            Assert.That(CompanionEngagementMode.StanceByte(CompanionStance.Defensive), Is.EqualTo(1));
            Assert.That(CompanionEngagementMode.StanceByte(CompanionStance.Passive), Is.EqualTo(2));
            Player player = Actor<Player>();
            Assert.That(CompanionEngagementMode.StanceOf(player), Is.EqualTo(CompanionStance.Aggressive), "default (a new login)");
            CompanionEngagementMode.Set(player, CompanionStance.Passive);
            Assert.That(CompanionEngagementMode.StanceOf(player), Is.EqualTo(CompanionStance.Passive));
            Assert.That(CompanionEngagementMode.IsPassive(player), Is.True);
            Assert.That(CompanionEngagementMode.IsDefensive(player), Is.False);
            CompanionEngagementMode.Set(player, true);
            Assert.That(CompanionEngagementMode.StanceOf(player), Is.EqualTo(CompanionStance.Defensive));
            Assert.That(CompanionEngagementMode.IsPassive(player), Is.False);
            CompanionEngagementMode.Set(player, false);
            Assert.That(CompanionEngagementMode.StanceOf(player), Is.EqualTo(CompanionStance.Aggressive));
            Assert.That(CompanionEngagementMode.IsPassive(null), Is.False);
        }

        [Test] public void PassiveStopsTheCompanionAndItsPet_AggressiveAndDefensiveUnchanged()
        {
            var (player, _, damage, _, enemy) = Party();
            PetBrain pet = AttachPet(damage);
            enemy.TestX = 100;
            Assert.That(CompanionEngagementMode.Allows(damage, enemy), Is.True, "aggressive");
            Assert.That(CompanionEngagementMode.Allows(pet.Body, enemy), Is.True, "aggressive pet");
            CompanionEngagementMode.Set(player, true);
            Assert.That(CompanionEngagementMode.Allows(damage, enemy), Is.True, "defensive, within 350");
            Assert.That(CompanionEngagementMode.Allows(pet.Body, enemy), Is.True, "defensive pet, within 350");
            Assert.That(CompanionEngagementMode.PassiveLeader(damage), Is.Null);
            CompanionEngagementMode.Set(player, CompanionStance.Passive);
            Assert.That(CompanionEngagementMode.PassiveLeader(damage), Is.SameAs(player));
            Assert.That(CompanionEngagementMode.PassiveLeader(pet.Body), Is.SameAs(player), "a companion's pet counts");
            Assert.That(CompanionEngagementMode.DefensiveLeader(damage), Is.Null);
            Assert.That(CompanionEngagementMode.Allows(damage, enemy), Is.False);
            Assert.That(CompanionEngagementMode.Allows(pet.Body, enemy), Is.False);
            Assert.That(B(damage).CanAggroTarget(enemy), Is.False);
            CompanionEngagementMode.Set(player, false);
            Assert.That(CompanionEngagementMode.Allows(damage, enemy), Is.True, "back to aggressive");
            Assert.That(CompanionEngagementMode.Allows(pet.Body, enemy), Is.True);
        }

        [Test] public void GamebotsAndTheHumansOwnPetAreNotPassive()
        {
            var (player, _, damage, _, enemy) = Party();
            Bot gamebot = MakeBot(new ClassMercenary());
            Add(player.Group, gamebot);
            gamebot.EnterPlayerLedGroup(player);
            Field(typeof(GameBot), gamebot, "<IsAutonomousWorldBot>k__BackingField", true);
            PetBrain humanPet = AttachPet(player);
            Bot outsider = MakeBot(new ClassMercenary());   // a world bot in no group
            Field(typeof(GameBot), outsider, "<IsAutonomousWorldBot>k__BackingField", true);
            CompanionEngagementMode.Set(player, CompanionStance.Passive);
            enemy.TestX = 100;
            Assert.That(CompanionEngagementMode.Allows(damage, enemy), Is.False, "the companion is passive");
            Assert.That(CompanionEngagementMode.PassiveLeader(gamebot), Is.Null);
            Assert.That(CompanionEngagementMode.Allows(gamebot, enemy), Is.True, "an invited autonomous gamebot is untouched");
            Assert.That(CompanionEngagementMode.Allows(outsider, enemy), Is.True);
            Assert.That(CompanionEngagementMode.PassiveLeader(humanPet.Body), Is.Null);
            Assert.That(CompanionEngagementMode.Allows(humanPet.Body, enemy), Is.True, "the player's own pet obeys the player");
            Assert.That(B(gamebot).HoldCompanionPassive(), Is.False);
        }

        [Test] public void PassiveHoldDropsAggroTargetAndAttack_AndPutsPetsOnPassive()
        {
            var (player, _, damage, _, enemy) = Party();
            PetBrain pet = AttachPet(damage);
            enemy.TestX = 100;
            B(damage).AddToAggroList(enemy, 100);
            pet.AddToAggroList(enemy, 100);
            damage.TargetObject = enemy;
            damage.Attacking = true;
            Assert.That(B(damage).HasAggro, Is.True);
            Assert.That(B(damage).HoldCompanionPassive(), Is.False, "not passive yet");
            CompanionEngagementMode.Set(player, CompanionStance.Passive);
            Assert.That(B(damage).HoldCompanionPassive(), Is.True);
            Assert.That(B(damage).HasAggro, Is.False);
            Assert.That(damage.TargetObject, Is.Null);
            Assert.That(damage.IsAttacking, Is.False);
            Assert.That(pet.HasAggro, Is.False);
            Assert.That(pet.AggressionState, Is.EqualTo(eAggressionState.Passive));
            Assert.That(pet.Disengages, Is.GreaterThan(0), "the pet is called off");
            B(damage).AddToAggroList(enemy, 100);
            Assert.That(B(damage).HasAggro, Is.False, "nothing new is added while passive");
            CompanionEngagementMode.Set(player, true);
            Assert.That(pet.AggressionState, Is.EqualTo(eAggressionState.Defensive), "leaving passive restores the pet");
        }

        [Test] public void GrindAndPullAreRefusedWhilePassive()
        {
            var (player, tank, _, _, enemy) = Party();
            CompanionEngagementMode.Set(player, CompanionStance.Passive);
            Assert.That(PlayerCompanionGrind.TryStart(player, out string message), Is.False);
            Assert.That(message, Does.Contain("passive"));
            Assert.That(PlayerCompanionGrind.IsActive(player), Is.False);
            Assert.That(PlayerLedPullCoordinator.Begin(player, enemy), Does.Contain("passive"));
            Assert.That(B(tank).Pulls, Is.Zero);
            CompanionEngagementMode.Set(player, false);
            Assert.That(PlayerLedPullCoordinator.Begin(player, enemy), Does.Contain("leads"));
            Assert.That(B(tank).Pulls, Is.EqualTo(1));
        }

        private (Player, Bot, Bot, Bot, Enemy) Party()
        {
            Player player = Actor<Player>();
            var group = new Group(player);
            Add(group, player);
            Bot tank = MakeBot(new ClassPaladin()), damage = MakeBot(new ClassCabalist()), healer = MakeBot(new ClassCleric());
            foreach (Bot bot in new[] { tank, damage, healer })
            {
                Add(group, bot);
                bot.EnterPlayerLedGroup(player);
                Field(typeof(GameBot), bot, "<IsTemporaryGroupHelper>k__BackingField", true);
                Field(typeof(GameBot), bot, "<Owner>k__BackingField", player);
            }
            return (player, tank, damage, healer, Actor<Enemy>());
        }
        private Bot MakeBot(ICharacterClass characterClass)
        {
            Bot bot = Actor<Bot>();
            bot.Alive = true;                   // field initializers do not run on an uninitialized object
            bot.TestRealm = eRealm.Albion;
            bot.Level = 1;
            bot.Name = characterClass.Name;
            bot.TestClass = characterClass;
            var brain = new Brain { Body = bot };
            Field(typeof(GameNPC), bot, "m_ownBrain", brain);
            return bot;
        }
        private PetBrain AttachPet(GameLiving owner)
        {
            Enemy pet = Actor<Enemy>();
            PetBrain brain = new(owner) { Body = pet };
            owner.ControlledBrain = brain;
            Field(typeof(GameNPC), pet, "m_ownBrain", brain);
            return brain;
        }
        private T Actor<T>() where T : GameLiving
        {
            T actor = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            actor.ObjectState = GameObject.eObjectState.Active;
            Field(typeof(GameLiving), actor, "<TempProperties>k__BackingField", new PropertyCollection());
            if (actor is GameNPC npc)
            {
                Field(typeof(GameNPC), actor, "m_brains", new ArrayList());
                Field(typeof(GameNPC), actor, "m_spells", new List<Spell>());
                npc.movementComponent = new NpcMovementComponent(npc);
            }
            actor.effectListComponent = EffectListComponent.Create(actor);
            _actors.Add(actor);
            return actor;
        }
        private static Brain B(Bot bot) => (Brain)bot.Brain;
        private static List<GameLiving> Members(Group group) => (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);
        private static void Add(Group group, GameLiving member) { Members(group).Add(member); member.Group = group; }
        private static void Field(Type type, object owner, string name, object value) => type.GetField(name, Hidden).SetValue(owner, value);
    }
}
