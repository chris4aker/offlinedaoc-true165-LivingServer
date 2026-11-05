using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Owner 2026-11-04: grouped with bots, when a bot hit a monster before the player or the player's pet did, the
    /// monster vanished with no kill credit and no loot. The server log showed it:
    /// "Critical error encountered in NpcService: NullReferenceException at BafFriends.InBafFight(GamePlayer puller)
    /// at StandardMobBrain.BringFriends ... Calling RemoveFromWorld with (entityOwner: GameNPC name=bear ...)".
    /// The monster's first think after the bot's hit asks whom the pull counts for. For a companion that read
    /// GameBot.Owner, which only /spawn helpers have: a hired companion or an invited gamebot gave null, and the NPC
    /// service's error handler removed the monster. These tests build a player's group with random companions (and
    /// pets), let a random member land the first hit, and check the pull resolves to the human player.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_BafCompanionFirstHit
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private bool _classic;
        private bool _companionsCount;
        private readonly List<long> _hiredIds = new();
        private long _nextId = 9_100_000;
        private DOL.UnitTests.EpicTestServerScope _server;

        private sealed class TestPlayer : GamePlayer
        {
            private TestPlayer() : base(null, null) { }
            public override bool InCombat => false;
        }

        [SetUp]
        public void Setup()
        {
            _server = new DOL.UnitTests.EpicTestServerScope(); // GameObject's quest cache reads an (empty) database
            _classic = Properties.CLASSIC165_MODE;
            _companionsCount = CompanionBringAFriend.Enabled;
            Properties.CLASSIC165_MODE = true;
            CompanionBringAFriend.Enabled = true;
        }

        [TearDown]
        public void TearDown()
        {
            _server?.Dispose();
            Properties.CLASSIC165_MODE = _classic;
            CompanionBringAFriend.Enabled = _companionsCount;
            var hired = HiredTable();
            foreach (long id in _hiredIds)
                hired.TryRemove(id, out _);
            _hiredIds.Clear();
        }

        [Test]
        public void AHiredCompanionHasNoOwnerYetCountsForItsPlayer()
        {
            GamePlayer player = Player();
            GameBot hired = Hired(player);
            Assert.Multiple(() =>
            {
                Assert.That(hired.Owner, Is.Null, "the old code read this: only /spawn helpers have an Owner");
                Assert.That(CompanionBringAFriend.IsCompanion(hired), Is.True);
                Assert.That(BafFriends.PullerPlayer(hired, out GameLiving countFrom), Is.SameAs(player));
                Assert.That(countFrom, Is.SameAs(hired), "the group is counted around the companion that pulled");
            });
        }

        [Test]
        public void AnInvitedGamebotCountsForThePlayerWhoLeadsTheGroup()
        {
            GamePlayer player = Player();
            GameBot invited = Invited(player);
            Assert.That(invited.Owner, Is.Null);
            Assert.That(BafFriends.PullerPlayer(invited, out _), Is.SameAs(player));
        }

        [Test]
        public void ASpawnHelperStillCountsForItsOwner()
        {
            GamePlayer player = Player();
            Assert.That(BafFriends.PullerPlayer(SpawnHelper(player), out _), Is.SameAs(player));
        }

        [Test]
        public void TheCompanionsPetsAndSubPetsCountForThePlayer()
        {
            GamePlayer player = Player();
            GameBot hired = Hired(player);
            GameNPC pet = Pet(hired);
            GameNPC subPet = Pet(pet);
            Assert.Multiple(() =>
            {
                Assert.That(BafFriends.PullerPlayer(pet, out GameLiving petCount), Is.SameAs(player),
                    "a hired companion's pet used to resolve to nobody (GetPlayerOwner reads the bot's Owner)");
                Assert.That(petCount, Is.SameAs(hired));
                Assert.That(BafFriends.PullerPlayer(subPet, out GameLiving subCount), Is.SameAs(player));
                Assert.That(subCount, Is.SameAs(hired));
                Assert.That(BafFriends.PullerPlayer(Pet(player), out GameLiving ownCount), Is.SameAs(player));
                Assert.That(ownCount, Is.SameAs(player));
            });
        }

        [Test]
        public void GamebotsOnTheirOwnAndCompanionsWithTheSettingOffBringNobody()
        {
            GamePlayer player = Player();
            GameBot worldBot = WorldBot();
            Assert.That(BafFriends.PullerPlayer(worldBot, out _), Is.Null, "a gamebot-led fight never triggers it");
            Assert.That(BafFriends.PullerPlayer(Pet(worldBot), out _), Is.Null);
            CompanionBringAFriend.Enabled = false;
            Assert.That(BafFriends.PullerPlayer(Hired(player), out _), Is.Null);
            Assert.That(BafFriends.PullerPlayer(Player(), out _), Is.Not.Null, "players always count");
        }

        [Test]
        public void NoPlayerMeansNoFightAndNoCrash()
        {
            Assert.That(() => BafFriends.InBafFight(null), Throws.Nothing);
            Assert.That(BafFriends.InBafFight(null), Is.False);
            Assert.That(() => BafFriends.MarkBafFight(null), Throws.Nothing);
        }

        /// <summary>
        /// A human with a pet, grouped with 1 to 7 random companions (hired, invited gamebots, /spawn helpers, some with
        /// pets); a random member or pet hits the monster first. Whoever it is, the pull counts for the human and the
        /// monster's first think can't fail.
        /// </summary>
        [Test]
        public void ARandomBotLandingTheFirstHitAlwaysCountsForTheHuman([Range(1, 40)] int seed)
        {
            var random = new Random(seed);
            GamePlayer player = Player();
            var attackers = new List<GameLiving> { player, Pet(player) };
            int bots = random.Next(1, 8);
            for (int i = 0; i < bots; i++)
            {
                GameBot bot = random.Next(3) switch { 0 => Hired(player), 1 => Invited(player), _ => SpawnHelper(player) };
                attackers.Add(bot);
                if (random.Next(2) == 0)
                    attackers.Add(Pet(bot));
            }

            List<GameLiving> botsFirst = attackers.Where(a => a is not GamePlayer && !IsPlayersOwnPet(a, player))
                .OrderBy(_ => random.Next()).ToList();
            GameLiving firstHit = botsFirst[0];

            GamePlayer resolved = null;
            Assert.That(() => resolved = BafFriends.PullerPlayer(firstHit, out _), Throws.Nothing);
            Assert.That(resolved, Is.SameAs(player), $"seed {seed}: first hit by {Describe(firstHit)}");
            Assert.That(() => BafFriends.InBafFight(resolved), Throws.Nothing);
        }

        private static bool IsPlayersOwnPet(GameLiving living, GamePlayer player) =>
            living is GameNPC npc && npc.Brain is ControlledMobBrain brain && brain.Owner == player;

        private static string Describe(GameLiving living) => living switch
        {
            GameBot bot when HiredCompanions165.IsHired(bot) => "a hired companion",
            GameBot bot when bot.IsInvitedGamebot165 => "an invited gamebot",
            GameBot => "a /spawn helper",
            GameNPC npc when npc.Brain is ControlledMobBrain { Owner: GameBot } => "a companion's pet",
            _ => living.GetType().Name,
        };

        private static GamePlayer Player()
        {
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(TestPlayer));
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden).SetValue(player, new PropertyCollection());
            return player;
        }

        private static GameBot NewBot()
        {
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden).SetValue(bot, new PropertyCollection());
            typeof(GameNPC).GetField("m_brains", Hidden).SetValue(bot, new ArrayList());
            return bot;
        }

        private static void SetBot(GameBot bot, string property, object value) =>
            typeof(GameBot).GetField($"<{property}>k__BackingField", Hidden).SetValue(bot, value);

        private GameBot Hired(GamePlayer player)
        {
            GameBot bot = NewBot();
            bot.DatabaseID = _nextId++;
            SetBot(bot, nameof(GameBot.IsAutonomousWorldBot), true);
            SetBot(bot, nameof(GameBot.PlayerGroupLeader), player);
            HiredTable()[bot.DatabaseID] = new OfflineHiredCompanionRecord { BotId = bot.DatabaseID };
            _hiredIds.Add(bot.DatabaseID);
            return bot;
        }

        private GameBot Invited(GamePlayer player)
        {
            GameBot bot = WorldBot();
            SetBot(bot, nameof(GameBot.PlayerGroupLeader), player);
            return bot;
        }

        private static GameBot SpawnHelper(GamePlayer player)
        {
            GameBot bot = NewBot();
            SetBot(bot, nameof(GameBot.IsTemporaryGroupHelper), true);
            SetBot(bot, nameof(GameBot.Owner), player);
            return bot;
        }

        private GameBot WorldBot()
        {
            GameBot bot = NewBot();
            bot.DatabaseID = _nextId++;
            SetBot(bot, nameof(GameBot.IsAutonomousWorldBot), true);
            return bot;
        }

        private static GameNPC Pet(GameLiving owner)
        {
            var brain = (ControlledMobBrain)RuntimeHelpers.GetUninitializedObject(typeof(ControlledMobBrain));
            typeof(ControlledMobBrain).GetField("m_owner", Hidden).SetValue(brain, owner);
            var pet = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            typeof(GameNPC).GetField("m_brains", Hidden).SetValue(pet, new ArrayList { brain });
            return pet;
        }

        private static ConcurrentDictionary<long, OfflineHiredCompanionRecord> HiredTable() =>
            (ConcurrentDictionary<long, OfflineHiredCompanionRecord>)typeof(HiredCompanions165)
                .GetField("Hired", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    }
}
