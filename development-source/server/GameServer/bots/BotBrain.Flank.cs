using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS;
using DOL.GS.Styles;

namespace DOL.AI.Brain
{
    /// <summary>
    /// A damage dealer steps to the back (or side) of a monster that is fighting someone else, so its back and side styles
    /// and the chains after them fire (owner 2026-11-05 class audit; Prima: Berserkers, Blademasters, Mercenaries,
    /// Infiltrators attack from behind while the tank holds the monster). At most four seconds a try, only when the bot
    /// knows such a style for the weapon in hand; again when the monster turned more than 45 degrees or moved more than
    /// 100 units, at most every three seconds (owner 2026-11-06). Gamebot tanks keep facing their monster; a companion
    /// tank flanks when someone else is the main tank. A companion's "Positioning" AI Option: Automatic (flank a monster
    /// the main tank holds), Flank (any monster not on itself, unless it is the main tank) or Stay in front. True 1.65 Mode.
    /// </summary>
    public partial class BotBrain
    {
        private const long FlankMilliseconds = 4_000;
        private const int FlankStandOff = 80;
        public const long FlankRetryMilliseconds = 3_000;
        public const int FlankRetryTurn = 512;      // 45 degrees in heading units (4096 a circle)
        public const int FlankRetryMove = 100;
        private GameLiving _flankTarget;
        private GameLiving _flankDoneFor;
        private long _flankSince;
        private long _flankDoneAt;
        private ushort _flankDoneHeading;
        private Vector2 _flankDonePosition;

        /// <summary>A bot that knows a back or side style (the "Positioning" AI Option is offered then).</summary>
        public static bool KnowsFlankStyle(GameBot bot) => bot?.Styles?.Any(style => style != null &&
            !style.StealthRequirement && style.OpeningRequirementType == Style.eOpening.Positional &&
            (Style.eOpeningPosition)style.OpeningRequirementValue is Style.eOpeningPosition.Back or
                Style.eOpeningPosition.Side) == true;

        /// <summary>
        /// Pure rule: whether the bot steps to the flank of a monster fighting someone else. A gamebot: any class but a
        /// tank. A player's companion by its "Positioning" choice: 0 Automatic, only on a monster the main tank holds (a
        /// tank companion then too: someone else is the main tank); 1 Flank, any monster unless it is the main tank itself;
        /// 2 Stay in front, never.
        /// </summary>
        public static bool FlankAllowed(int choice, bool companion, bool tankClass, bool victimIsMainTank) =>
            !companion ? !tankClass : choice switch
            {
                2 => false,
                1 => !tankClass || victimIsMainTank,
                _ => victimIsMainTank,
            };

        /// <summary>Pure rule: a finished flank is tried again once the monster turned or moved away from it.</summary>
        public static bool FlankRetry(long sinceDone, ushort headingThen, ushort headingNow, float moved)
        {
            if (sinceDone < FlankRetryMilliseconds)
                return false;
            int turn = System.Math.Abs(headingNow % 4096 - headingThen % 4096);
            return System.Math.Min(turn, 4096 - turn) > FlankRetryTurn || moved > FlankRetryMove;
        }

        /// <summary>The back style (preferred) or side style this bot could use on its target with the weapon in hand.</summary>
        private Style FlankStyle()
        {
            GameBot bot = BotBody;
            if (bot?.Styles == null)
                return null;
            DbInventoryItem left = bot.Inventory?.GetItem(eInventorySlot.LeftHandWeapon);
            return bot.Styles
                .Where(style => style != null && !style.StealthRequirement && style.Level <= bot.Level &&
                                style.OpeningRequirementType == Style.eOpening.Positional &&
                                (Style.eOpeningPosition)style.OpeningRequirementValue is Style.eOpeningPosition.Back or
                                    Style.eOpeningPosition.Side &&
                                BotMeleeStylePolicy.MatchesWeapon(style, bot.ActiveWeapon, left, bot.ActiveWeaponSlot) &&
                                (BotMeleeStylePolicy.MayTaunt(bot) || BotMeleeStylePolicy.ThreatSign(style) <= 0))
                .OrderBy(style => (Style.eOpeningPosition)style.OpeningRequirementValue == Style.eOpeningPosition.Back ? 0 : 1)
                .ThenByDescending(style => style.GrowthRate)
                .FirstOrDefault();
        }

        private static bool InPosition(GameLiving target, GameLiving bot, Style.eOpeningPosition position)
        {
            float angle = target.GetAngle(bot);
            return position == Style.eOpeningPosition.Back
                ? angle is > 150 and < 210
                : angle is >= 45 and <= 150 or >= 210 and <= 315;
        }

        /// <summary>Walks the bot to the flank of its target; true while that walk owns the turn.</summary>
        private bool TryFlank(GameLiving target)
        {
            GameBot bot = BotBody;
            if (Classic165Mode.IsOn && target != null && target == _flankDoneFor && FlankRetry(
                    GameLoop.GameLoopTime - _flankDoneAt, _flankDoneHeading, target.Heading,
                    Vector2.Distance(_flankDonePosition, new(target.X, target.Y))))
                _flankDoneFor = null;
            if (!Classic165Mode.IsOn || bot == null || target is not GameNPC || target is GameBot || !target.IsAlive ||
                target == _flankDoneFor || PrefersCurrentSpellRange() ||
                bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance || bot.IsStealthed ||
                target.TargetObject is not GameLiving victim || victim == bot || !victim.IsAlive ||
                !bot.IsWithinRadius(target, 400) ||
                !FlankAllowed(CompanionOptions165.Choice(bot, "position"), BotMeleeStylePolicy.TauntRulesPlayer(bot) != null,
                    BotPartyRoles.IsTank(bot), BotMeleeStylePolicy.IsMainTankOn(victim, target)))
            {
                _flankTarget = null;
                return false;
            }
            Style style = FlankStyle();
            if (style == null)
                return Done();
            var position = (Style.eOpeningPosition)style.OpeningRequirementValue;
            if (InPosition(target, bot, position))
                return Done();

            long now = GameLoop.GameLoopTime;
            if (_flankTarget != target)
            {
                _flankTarget = target;
                _flankSince = now;
            }
            if (now - _flankSince > FlankMilliseconds)
                return Done();

            // the side nearer to the bot, or straight behind
            ushort heading = position == Style.eOpeningPosition.Back
                ? (ushort)((target.Heading + 2048) % 4096)
                : (ushort)((target.Heading + ((target.GetAngle(bot) is > 180 and < 360) ? 3072 : 1024)) % 4096);
            Point2D spot = target.GetPointFromHeading(heading, FlankStandOff);
            if (Vector2.DistanceSquared(new(bot.X, bot.Y), new(spot.X, spot.Y)) <= 30 * 30)
                return Done();
            if (bot.attackComponent.AttackState)
                bot.StopAttack();
            bot.PathTo(new Vector3(spot.X, spot.Y, target.Z), bot.MaxSpeed);
            return true;

            bool Done()
            {
                _flankDoneFor = target;
                _flankDoneAt = GameLoop.GameLoopTime;
                _flankDoneHeading = target.Heading;
                _flankDonePosition = new(target.X, target.Y);
                _flankTarget = null;
                if (bot.attackComponent.AttackState == false && bot.TargetObject == target)
                    bot.TurnTo(target);
                return false;
            }
        }
    }
}
