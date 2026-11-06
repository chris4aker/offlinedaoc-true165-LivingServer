using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Class build plans against period (2002-2004) templates: how often each build rolls and what it trains.</summary>
    [TestFixture, NonParallelizable]
    public class UT_ClassBuilds165
    {
        // Weapon lines resolve through SkillBase, which needs a server and its language strings.
        private sealed class SpecTestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private static readonly IObjectDatabase EmptyDatabase =
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previousServer;
        private PetTestLanguageScope _language;

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            _language = new PetTestLanguageScope();
            GameServer.LoadTestDouble((SpecTestServer)RuntimeHelpers.GetUninitializedObject(typeof(SpecTestServer)));
        }

        [TearDown]
        public void TearDown()
        {
            GameServer.LoadTestDouble(_previousServer);
            _language.Dispose();
        }

        private const int Samples = 6000;

        /// <summary>New bots of the class as the world rolls them: weighted family, then the class's own variants.</summary>
        private static List<BotSpec> Roll(eCharacterClass characterClass) =>
            Enumerable.Range(0, Samples).Select(_ => BotSpec.GetSpec(characterClass, BotSpec.ChooseRandomSpecialization(characterClass))).ToList();

        private static double Share(List<BotSpec> plans, Func<BotSpec, bool> match) => plans.Count(match) / (double)plans.Count;

        private static uint Cap(BotSpec plan, string line) => plan.SpecLines.FirstOrDefault(l => l.Spec == line).SpecCap;

        private static Dictionary<string, int> At(BotSpec plan, eCharacterClass characterClass, int level) =>
            UT_BotSpecTrainer.Level(plan, characterClass, level);

        [Test]
        public void MercenaryShieldIsOneInEightAndSlashLeads()
        {
            List<BotSpec> plans = Roll(eCharacterClass.Mercenary);
            Assert.That(Share(plans, p => p.SpecType == eSpecType.DualWieldAndShield), Is.EqualTo(1 / 8.0).Within(0.03));
            Assert.That(Share(plans, p => p.WeaponOneType == eObjectType.SlashingWeapon), Is.EqualTo(36 / 69.0).Within(0.03));
            Assert.That(Share(plans, p => p.WeaponOneType == eObjectType.ThrustWeapon), Is.EqualTo(22 / 69.0).Within(0.03));
            Assert.That(Share(plans, p => p.WeaponOneType == eObjectType.CrushingWeapon), Is.EqualTo(11 / 69.0).Within(0.03));
        }

        [Test]
        public void PaladinIsMostlyOneHandSlashWithChantsAndShield42()
        {
            List<BotSpec> plans = Roll(eCharacterClass.Paladin);
            Assert.That(Share(plans, p => p.Is2H), Is.EqualTo(0.15).Within(0.04));
            Assert.That(Share(plans, p => p.WeaponOneType == eObjectType.SlashingWeapon), Is.EqualTo(0.8).Within(0.03));
            foreach (BotSpec plan in plans.Where(p => !p.Is2H).Take(200))
            {
                Assert.That(Cap(plan, Specs.Chants), Is.InRange(46u, 48u));
                Assert.That(Cap(plan, Specs.Shields), Is.EqualTo(42u));
                Assert.That(Cap(plan, SkillBase.ObjectTypeToSpec(plan.WeaponOneType)), Is.InRange(29u, 44u));
            }
        }

        [Test]
        public void ArmsmanTwoHanderIsMostlyPolearmAndCrushIsRare()
        {
            List<BotSpec> plans = Roll(eCharacterClass.Armsman);
            Assert.That(Share(plans, p => p.WeaponTwoType == eObjectType.PolearmWeapon), Is.EqualTo(0.85).Within(0.03));
            Assert.That(Share(plans, p => p.WeaponOneType == eObjectType.CrushingWeapon), Is.EqualTo(0.05).Within(0.02));
        }

        [Test]
        public void TheurgistIsHalfAirAndIceTakesEarthSecond()
        {
            List<BotSpec> plans = Roll(eCharacterClass.Theurgist);
            Assert.That(Share(plans, p => p.SpecType == eSpecType.AirTheur), Is.EqualTo(0.5).Within(0.03));
            foreach (BotSpec plan in plans.Where(p => p.SpecType == eSpecType.IceTheur).Take(100))
            {
                Dictionary<string, int> at50 = At(plan, eCharacterClass.Theurgist, 50);
                Assert.That(at50[Specs.Cold_Magic], Is.GreaterThanOrEqualTo(46));
                Assert.That(at50[Specs.Earth_Magic], Is.InRange(20, 26));
                Assert.That(at50[Specs.Earth_Magic], Is.GreaterThan(at50[Specs.Wind_Magic]));
            }
        }

        [Test]
        public void FriarMainBuildIsEnhancementStaffAndRejuvenationIsRare()
        {
            List<BotSpec> plans = Roll(eCharacterClass.Friar);
            Assert.That(Share(plans, p => p.SpecType == eSpecType.RejuvFriar), Is.EqualTo(0.1).Within(0.03));
            BotSpec main = BotSpec.GetSpec(eCharacterClass.Friar, eSpecType.EnhanceFriar);
            Dictionary<string, int> at50 = At(main, eCharacterClass.Friar, 50);
            Assert.That(new[] { at50[Specs.Enhancement], at50[Specs.Staff], at50[Specs.Rejuvenation], at50[Specs.Parry] },
                Is.EqualTo(new[] { 49, 39, 15, 13 }));
        }
    }
}
