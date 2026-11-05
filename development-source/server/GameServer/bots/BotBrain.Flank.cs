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
    /// Infiltrators attack from behind while the tank holds the monster). Once per target, at most four seconds, only when
    /// the bot knows such a style for the weapon in hand; tanks keep facing their monster. True 1.65 Mode.
    /// </summary>
    public partial class BotBrain
    {
        private const long FlankMilliseconds = 4_000;
        private const int FlankStandOff = 80;
        private GameLiving _flankTarget;
        private GameLiving _flankDoneFor;
        private long _flankSince;

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
            if (!Classic165Mode.IsOn || bot == null || target is not GameNPC || target is GameBot || !target.IsAlive ||
                target == _flankDoneFor || BotPartyRoles.IsTank(bot) || PrefersCurrentSpellRange() ||
                bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance || bot.IsStealthed ||
                target.TargetObject is not GameLiving victim || victim == bot || !victim.IsAlive ||
                !bot.IsWithinRadius(target, 400))
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
                _flankTarget = null;
                if (bot.attackComponent.AttackState == false && bot.TargetObject == target)
                    bot.TurnTo(target);
                return false;
            }
        }
    }
}
