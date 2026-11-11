using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.Tests.Unit.Population
{
    /// <summary>Owner 2026-11-10 "level1": while a realm fills, new level-1 bots come at most 4 a realm a minute once the
    /// realm is half full and its 1-9 share is met (fakes from UT_PopulationScheduler).</summary>
    [TestFixture, NonParallelizable]
    public class UT_PopulationFillPace
    {
        private FakePopulationClock _clock;
        private FakePopulationArchive _archive;
        private FakePopulationLifecycle _life;
        private FakePopulationWorld _world;
        private AutonomousPopulationScheduler _s;

        [SetUp]
        public void Setup()
        {
            _clock = new FakePopulationClock();
            _archive = new FakePopulationArchive();
            _life = new FakePopulationLifecycle(_archive, _clock);
            _world = new FakePopulationWorld();
        }

        private void Start(int alb, int mid, int hib)
        {
            _s = new AutonomousPopulationScheduler(_life, _archive, _world, _clock, new Random(11), null);
            _s.ApplySettings(PopulationGoalSettings.Default with { Albion = alb, Midgard = mid, Hibernia = hib, Rotation = "Normal" }, null);
        }

        private void Run(int seconds)
        {
            for (int i = 0; i < seconds; i++)
            {
                _clock.UtcNow = _clock.UtcNow.AddSeconds(1);
                _s.Tick();
            }
        }

        private void AddLive(eRealm realm, int count, int level)
        {
            for (int i = 0; i < count; i++) _life.PutOnline(_archive.Add(realm, level, now: _clock.UtcNow));
        }

        /// <summary>New level-1 bots made per realm since <paramref name="afterId"/> (the fake archive numbers bots in order).</summary>
        private Dictionary<eRealm, int> CreatedSince(long afterId) =>
            AutonomousPopulationScheduler.Realms.ToDictionary(realm => realm,
                realm => _archive.Bots.Values.Count(b => b.Id > afterId && b.Realm == realm && b.Level == 1));

        [Test]
        public void TokensRefillFourAMinuteAndNeverSaveMoreThanFour()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousPopulationFillPace.NextTokens(0, 60), Is.EqualTo(4).Within(1e-9));
                Assert.That(AutonomousPopulationFillPace.NextTokens(3.5, 10), Is.EqualTo(4).Within(1e-9));
                Assert.That(AutonomousPopulationFillPace.NextTokens(0, 10), Is.EqualTo(4 / 6.0).Within(1e-9));
                Assert.That(AutonomousPopulationFillPace.NextTokens(-2, -5), Is.EqualTo(0).Within(1e-9));
            });
        }

        [Test]
        public void AMostlyEmptyRealmAndTheOwnLevelOneShareAreNotPaced()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousPopulationFillPace.FreeNew(100, 500, 0, 60), Is.EqualTo(AutonomousPopulationFillPace.Unlimited), "under half");
                Assert.That(AutonomousPopulationFillPace.FreeNew(250, 500, 10, 60), Is.EqualTo(50), "half full: the 1-9 share only");
                Assert.That(AutonomousPopulationFillPace.FreeNew(500, 600, 72, 72), Is.EqualTo(0));
                Assert.That(AutonomousPopulationFillPace.FreeNew(500, 600, 90, 72), Is.EqualTo(0));
                Assert.That(AutonomousPopulationFillPace.Allowed(30, 0, 4.9), Is.EqualTo(4));
                Assert.That(AutonomousPopulationFillPace.Allowed(30, 10, 2.5), Is.EqualTo(12));
                Assert.That(AutonomousPopulationFillPace.Allowed(5, 10, 0), Is.EqualTo(5));
                Assert.That(AutonomousPopulationFillPace.Allowed(0, 10, 4), Is.EqualTo(0));
                Assert.That(AutonomousPopulationFillPace.ReplacementBudget(false, 500, 600, 72, 72), Is.EqualTo(AutonomousPopulationFillPace.Unlimited));
                Assert.That(AutonomousPopulationFillPace.ReplacementBudget(true, 500, 600, 72, 72), Is.EqualTo(0),
                    "owner 2026-11-10: the paced tokens are Fill's only, never a replacement for a session end");
                Assert.That(AutonomousPopulationFillPace.ReplacementBudget(true, 500, 600, 60, 72), Is.EqualTo(12), "the free 1-9 share");
                Assert.That(AutonomousPopulationFillPace.ReplacementBudget(true, 100, 600, 0, 72), Is.EqualTo(AutonomousPopulationFillPace.Unlimited));
                Assert.That(AutonomousPopulationFillPace.Enabled, Is.True, "switch defaults on");
            });
        }

        [Test]
        public void RestartWithRaisedGoalCreatesAtMostFourLevelOnesPerRealmPerMinute()
        {
            // 11/10 12:19: goals 500 -> 600 at a restart, too few saved bots for the higher brackets (here none at all).
            foreach (eRealm realm in AutonomousPopulationScheduler.Realms)
            {
                AddLive(realm, 72, 5);     // the 1-9 share of 600 is already there
                AddLive(realm, 428, 15);
            }
            long baseline = _archive.Bots.Keys.Max();
            Start(600, 600, 600);

            Run(60);
            Dictionary<eRealm, int> firstMinute = CreatedSince(baseline);
            Assert.That(firstMinute.Values, Is.All.InRange(1, 8), "4 saved tokens plus 4 more in the first minute");

            int previous = 0;
            var perMinute = new List<int>();
            for (int minute = 2; minute <= 10; minute++)
            {
                Run(60);
                int now = CreatedSince(baseline)[eRealm.Albion];
                perMinute.Add(now - (previous == 0 ? firstMinute[eRealm.Albion] : previous));
                previous = now;
            }
            Assert.That(perMinute, Is.All.LessThanOrEqualTo(5), "about 4 a minute once the saved tokens are spent");
            Assert.That(CreatedSince(baseline).Values, Is.All.InRange(30, 48), "ten minutes: about 40 a realm, not 100 at once");

            Run(30 * 60);
            foreach (eRealm realm in AutonomousPopulationScheduler.Realms)
                Assert.That(_life.LiveCount(realm), Is.InRange(590, 605), realm + " still reaches its goal");
        }

        [Test]
        public void AFreshWorldWithAnEmptyArchiveFillsHalfAtTheLoginRateThenReachesItsGoal()
        {
            // No saved bots at all: every place is a new level 1. Half the goal comes at the login rate, the rest 4 a minute.
            Start(500, 0, 0);
            Run(10 * 60);
            Assert.That(_life.LiveCount(eRealm.Albion), Is.InRange(250, 310), "half at once, then paced");
            // X = 90 minutes for a 500 goal (about 65 expected; owner 2026-11-10: session ends no longer spend the paced
            // tokens, so they only end when a saved bot can replace them). Owner may pick X.
            Run(80 * 60);
            Assert.That(_life.LiveCount(eRealm.Albion), Is.InRange(490, 505));
        }

        [Test]
        public void SessionEndsDoNotSpendThePacedTokensSoAPacedRealmKeepsGrowing()
        {
            // Round-3 review (owner 2026-11-10): session ends were approved against the same 4-a-minute tokens Fill spends,
            // so with no saved bots a realm could stay flat below its goal. Now nobody leaves without a saved replacement.
            foreach (eRealm realm in AutonomousPopulationScheduler.Realms)
            {
                AddLive(realm, 72, 5);     // the 1-9 share of 600 is met
                AddLive(realm, 328, 15);   // well over half full: new level 1s are paced
            }
            Start(600, 600, 600);
            int lowest = int.MaxValue;
            for (int minute = 1; minute <= 30; minute++)
            {
                Run(60);
                lowest = Math.Min(lowest, _life.LiveCount(eRealm.Albion));
            }
            Assert.That(lowest, Is.GreaterThanOrEqualTo(398), "never dips below the start");
            foreach (eRealm realm in AutonomousPopulationScheduler.Realms)
                Assert.That(_life.LiveCount(realm), Is.InRange(500, 530), realm + ": about 4 a minute of real growth for 30 minutes");
        }

        [Test]
        public void SwitchedOffNewLevelOnesComeAtTheLoginRateAsBefore()
        {
            foreach (eRealm realm in AutonomousPopulationScheduler.Realms)
            {
                AddLive(realm, 72, 5);
                AddLive(realm, 428, 15);
            }
            long baseline = _archive.Bots.Keys.Max();
            AutonomousPopulationFillPace.Enabled = false;
            try
            {
                Start(600, 600, 600);
                Run(5 * 60);
                Assert.That(CreatedSince(baseline).Values, Is.All.GreaterThanOrEqualTo(90), "the old burst");
            }
            finally
            {
                AutonomousPopulationFillPace.Enabled = true;
            }
        }
    }
}
