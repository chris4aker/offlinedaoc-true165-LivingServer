using System;
using DOL.GS.Effects;
using DOL.GS.RealmAbilities;

namespace DOL.GS
{
    /// <summary>
    /// The stealth detection range a player has against a stealthed enemy (GamePlayer.CanDetect), shared with gamebots
    /// so both sides see a stealther by the same rules.
    /// </summary>
    public static class StealthDetection
    {
        public const int HardCap = 1900;

        /*
         * http://www.critshot.com/forums/showthread.php?threadid=3142
         * -Normal detection range = (enemy lvl - your stealth spec) * 20 + 125
         * -Detect Hidden Range = (enemy lvl - your stealth spec) * 50 + 250
         * -See Hidden range = 2700 - (36 * your stealth spec) (https://forums.freddyshouse.com/threads/scouts-and-stealth.139740/)
         * Hard cap 1,900.
         */
        public static int Range(int viewerLevel, int stealthSpec, bool detectHidden, bool seeHidden, int bonus)
        {
            int spec = Math.Min(50, stealthSpec);
            int levelDiff = Math.Max(0, viewerLevel - spec);
            int range = detectHidden ? levelDiff * 50 + 250 : levelDiff * 20 + 125;
            if (seeHidden)
                range = Math.Max(range, 2700 - 36 * spec);
            return Math.Min(range + bonus, HardCap);
        }

        /// <summary>The player rules for any viewer against a stealthed player or gamebot. True Sight sees everything;
        /// Vanish hides from everything else.</summary>
        public static bool CanDetect(GameLiving viewer, GameLiving stealthed)
        {
            if (viewer == null || stealthed == null || !stealthed.IsStealthed)
                return true;
            if (stealthed.Realm == viewer.Realm || stealthed.Group != null && stealthed.Group == viewer.Group)
                return true;
            if (viewer.effectListComponent?.ContainsEffectForEffectType(eEffect.TrueSight) == true)
                return true;
            if (stealthed.effectListComponent?.ContainsEffectForEffectType(eEffect.Vanish) == true ||
                stealthed is GamePlayer && stealthed.EffectList?.GetOfType<VanishEffect>() != null)
                return false;
            bool camouflage = stealthed.effectListComponent?.ContainsEffectForEffectType(eEffect.Camouflage) == true;
            bool detectHidden = viewer.HasAbility(Abilities.DetectHidden) && !stealthed.HasAbility(Abilities.DetectHidden) && !camouflage;
            bool seeHidden = viewer.HasAbilityType(typeof(AtlasOF_SeeHidden)) && ClassOf(stealthed)?.IsAssassin != true && !camouflage;
            int range = Range(viewer.Level, stealthed.GetModifiedSpecLevel(Specs.Stealth), detectHidden, seeHidden,
                viewer.BaseBuffBonusCategory[eProperty.Skill_Stealth]);
            return viewer.IsWithinRadius(stealthed, range);
        }

        /// <summary>
        /// A gamebot's target filters: a stealthed enemy is a target only when the bot detects it (True 1.65 Mode, as a
        /// player would); otherwise stealthed enemies stay invisible to bots as before. Only stealthed candidates pay.
        /// </summary>
        public static bool BotCanSee(GameLiving bot, GameLiving target) =>
            target != null && (!target.IsStealthed || Classic165Mode.IsOn && CanDetect(bot, target));

        private static ICharacterClass ClassOf(GameLiving living) => living switch
        {
            GamePlayer player => player.CharacterClass,
            GameBot bot => bot.CharacterClass,
            _ => null,
        };
    }
}
