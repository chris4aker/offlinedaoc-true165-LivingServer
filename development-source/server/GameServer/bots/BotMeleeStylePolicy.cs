using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Styles;

namespace DOL.GS
{
    /// <summary>Shared companion/world-bot selection; native openings and costs remain authoritative.</summary>
    public static class BotMeleeStylePolicy
    {
        // Positional (front/side/back) and stealth styles are eligible: StyleProcessor.CanUseStyle (checked in Usable)
        // only lets them through when the bot really stands at that side of the target, or is hidden, as for a player
        // (owner 2026-11-05 class audit: bots never used a single positional style, and every chain after one was dead).
        public static bool IsEligible(Style style) => style != null;

        /// <summary>A style whose proc raises (taunt) or lowers (detaunt) the attacker's threat.</summary>
        public static int ThreatSign(Style style)
        {
            if (style?.Procs == null)
                return 0;
            foreach (StyleProcInfo proc in style.Procs)
                if (proc?.Spell?.SpellType == eSpellType.StyleTaunt && proc.Spell.Value != 0)
                    return proc.Spell.Value > 0 ? 1 : -1;
            return 0;
        }

        /// <summary>
        /// Taunt styles are a tank's job. A damage dealer or hybrid grouped with a tank never taunts (Prima: damage dealers
        /// must not pull aggro off the tank); solo, or with no tank to hold the monster, it may. Before 2026-11-05 every
        /// melee bot taunted on every swing, because the taunt style is the highest-growth anytime style of nearly every
        /// weapon line.
        /// </summary>
        public static bool MayTaunt(GameBot bot) => MayTaunt(bot, bot?.TargetObject as GameLiving);

        /// <summary>
        /// <see cref="MayTaunt(GameBot)"/> for a given monster. True 1.65 Mode, a player's companion (owner 2026-11-06: "they
        /// can have like zero hps and they will still use a taunt style to taunt off you"): its "Taunt" AI Option decides -
        /// Always taunts, Never (detaunt) never does, and Automatic taunts only for a tank-class companion that is the main
        /// tank (the player is not: see <see cref="PlayerIsMainTank"/>) at or above 40% health.
        /// </summary>
        public static bool MayTaunt(GameBot bot, GameLiving target)
        {
            if (bot == null || target is GamePlayer or GameBot)
                return false;   // players have no aggro: taunt styles only waste endurance in RvR (period forums, Jun 2003)
            int choice = CompanionOptions165.Choice(bot, "aggro");
            if (TauntRulesPlayer(bot) is GamePlayer player)
                return CompanionMayTaunt(choice, BotPartyRoles.IsTank(bot), PlayerIsMainTank(player, target), bot.HealthPercent);
            return choice switch
            {
                1 => true,    // the player chose: hold the monster
                2 => false,   // the player chose: shed it
                _ => BotPartyRoles.IsTank(bot) || !HasTankPartner(bot),
            };
        }

        /// <summary>On Automatic a companion tank stops taunting under this health (it would only die holding the monster).</summary>
        public const int AutomaticTauntMinHealthPercent = 40;

        /// <summary>
        /// Pure rule for a player's companion: the "Taunt" option (0 Automatic, 1 Always, 2 Never (detaunt)). Automatic: only
        /// a tank-class companion taunts, never while the player is the main tank, never under 40% health.
        /// </summary>
        public static bool CompanionMayTaunt(int choice, bool tankClass, bool playerMainTank, int healthPercent) => choice switch
        {
            1 => true,
            2 => false,
            _ => tankClass && !playerMainTank && healthPercent >= AutomaticTauntMinHealthPercent,
        };

        /// <summary>Pure rule for a player's companion with the monster on it: give it back (detaunt styles).</summary>
        public static bool CompanionDetaunts(int choice, bool tankClass, bool hasTankPartner, bool playerMainTank) => choice switch
        {
            1 => false,
            2 => true,
            _ => playerMainTank || !tankClass && hasTankPartner,
        };

        /// <summary>The companion's player whose taunt rules apply (True 1.65 Mode), or null for a gamebot.</summary>
        public static GamePlayer TauntRulesPlayer(GameBot bot) => Classic165Mode.IsOn ? bot?.CompanionPlayer : null;

        private sealed class FightStart
        {
            public GamePlayer Player;
            public bool PlayerTanks;
            public long Seen;
        }

        // one small entry per monster a companion fights; weak keys, so a despawned monster takes its entry with it
        private static readonly ConditionalWeakTable<GameLiving, FightStart> FightStarts = new();

        /// <summary>A fight not looked at for this long starts afresh (the monster reset or was pulled again later).</summary>
        public const long FightStartMemoryMilliseconds = 15_000;

        /// <summary>
        /// Pure rule: the player is the main tank when a tank class, or when the monster was on them at the start of the
        /// fight while they fight in melee (a Berserker, Blademaster, Mercenary or Savage tanking). A caster the monster ran
        /// to after a nuke is not tanking: its companions still taunt the monster off it.
        /// </summary>
        public static bool MainTankAtStart(bool playerTankClass, bool monsterOnPlayer, bool playerCaster) =>
            playerTankClass || monsterOnPlayer && !playerCaster;

        /// <summary>
        /// True 1.65 Mode: whether the player is the main tank against this monster. Decided once per fight, when the monster
        /// first has a target (undecided before: false), and kept while companions keep looking at that fight.
        /// </summary>
        public static bool PlayerIsMainTank(GamePlayer player, GameLiving monster)
        {
            if (player?.CharacterClass == null)
                return false;
            if (BotHealTriage165.IsTank(player))
                return true;
            if (monster is not GameNPC || monster is GameBot)
                return false;
            long now = GameLoop.GameLoopTime;
            if (FightStarts.TryGetValue(monster, out FightStart start) && start.Player == player &&
                now - start.Seen <= FightStartMemoryMilliseconds)
            {
                start.Seen = now;
                return start.PlayerTanks;
            }
            if (monster.TargetObject is not GameLiving victim)
                return false;
            bool tanks = MainTankAtStart(false, victim == player,
                BotBrain.PrefersSpellRange(player.CharacterClass));
            FightStarts.AddOrUpdate(monster, new FightStart { Player = player, PlayerTanks = tanks, Seen = now });
            return tanks;
        }

        /// <summary>
        /// The main tank holding this monster: a tank-class bot, or a player who is the main tank (True 1.65 Mode). A stun
        /// style on that monster is not a peel (it would only waste the stun and the endurance).
        /// </summary>
        public static bool IsMainTankOn(GameLiving victim, GameLiving monster) => victim switch
        {
            GameBot tank => BotPartyRoles.IsTank(tank),
            GamePlayer player => Classic165Mode.IsOn && PlayerIsMainTank(player, monster),
            _ => false,
        };

        /// <summary>A style that stuns (Slam and the like).</summary>
        public static bool IsStunStyle(Style style)
        {
            if (style?.Procs == null)
                return false;
            foreach (StyleProcInfo proc in style.Procs)
                if (proc?.Spell?.SpellType == eSpellType.StyleStun)
                    return true;
            return false;
        }

        /// <summary>
        /// A stun style (Slam: a long stun for a lot of endurance) is used on purpose, as players did: to stop a cast, to
        /// peel the target off a realm mate, or to open on an enemy player. Not as a filler: its growth beats every anytime
        /// weapon style, so tanks slammed on every swing the stun allowed. True 1.65 Mode: a monster the main tank holds (a
        /// tank-class bot, or the player as main tank) is not peeled.
        /// </summary>
        public static bool StunWanted(GameLiving bot, GameLiving target, AttackData lastAttack) =>
            bot != null && target != null && (target.IsCasting ||
            target.TargetObject is GameLiving victim && victim != bot && victim.Realm == bot.Realm &&
                !(Classic165Mode.IsOn && target is GameNPC and not GameBot && IsMainTankOn(victim, target)) ||
            target is GamePlayer or GameBot && lastAttack?.Target != target);

        /// <summary>A non-tank the monster has turned on, with a living tank (bot tank class or player) in its group: detaunt.</summary>
        public static bool ShouldDetaunt(GameBot bot)
        {
            int aggro = CompanionOptions165.Choice(bot, "aggro");
            if (aggro == 1 || bot.TargetObject is not GameLiving target || target.TargetObject != bot)
                return false;
            // True 1.65 Mode, a player's companion: also when the player is the main tank (BotMeleeStylePolicy.MayTaunt)
            if (TauntRulesPlayer(bot) is GamePlayer player)
                return CompanionDetaunts(aggro, BotPartyRoles.IsTank(bot), HasTankPartner(bot), PlayerIsMainTank(player, target));
            return aggro == 2 || !BotPartyRoles.IsTank(bot) && HasTankPartner(bot);
        }

        public static bool HasTankPartner(GameBot bot)
        {
            if (bot?.Group == null || bot.Group.MemberCount <= 1)
                return false;
            foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
            {
                if (member == null || member == bot || !member.IsAlive || member.CurrentRegionID != bot.CurrentRegionID)
                    continue;
                if (member is GameBot other ? BotPartyRoles.IsTank(other) :
                    member is GamePlayer player && player.CharacterClass != null &&
                    BotPartyRoles.For((eCharacterClass)player.CharacterClass.ID) == BotPartyRole.Tank)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Swing value of a style: its growth, or for an opener whose follow-up the bot knows and can use, the average over
        /// both swings (a player opens a chain even when the opener alone hits softer than the plain best style).
        /// </summary>
        public static double Value(GameBot bot, Style style, bool mayTaunt)
        {
            double value = style.GrowthRate;
            if (bot?.Styles == null || style.ID == 0)
                return value;
            foreach (Style next in bot.Styles)
            {
                if (next == null || next.OpeningRequirementType != Style.eOpening.Offensive ||
                    next.OpeningRequirementValue != style.ID || next.Level > bot.Level ||
                    !mayTaunt && ThreatSign(next) > 0)
                    continue;
                value = System.Math.Max(value, (style.GrowthRate + next.GrowthRate) / 2);
            }
            return value;
        }

        public static bool Better(Style candidate, Style current) => current == null ||
            candidate.GrowthRate > current.GrowthRate ||
            candidate.GrowthRate == current.GrowthRate && candidate.Level > current.Level;

        public static DbInventoryItem Weapon(GameBot bot, Style style) =>
            style?.WeaponTypeRequirement == (int)eObjectType.Shield && bot.ActiveWeaponSlot == eActiveWeaponSlot.Standard
                ? bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon) : bot.ActiveWeapon;

        public static bool Usable(GameBot bot, Style style, AttackData lastAttack) =>
            IsEligible(style) && style.Level <= bot.Level &&
            MatchesWeapon(style, bot.ActiveWeapon, bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon), bot.ActiveWeaponSlot) &&
            StyleProcessor.CheckEnduranceCost(bot, Weapon(bot, style), style) &&
            bot.CheckStyleStun(style) &&
            StyleProcessor.CanUseStyle(lastAttack, bot, style, Weapon(bot, style));

        public static bool MatchesWeapon(Style style, DbInventoryItem primary, DbInventoryItem left, eActiveWeaponSlot slot)
        {
            if (style == null || primary == null || slot == eActiveWeaponSlot.Distance) return false;
            if (style.WeaponTypeRequirement == Style.SpecialWeaponType.DualWield)
            {
                if (slot != eActiveWeaponSlot.Standard || left == null ||
                    primary.Item_Type is not Slot.RIGHTHAND and not Slot.LEFTHAND ||
                    left.Object_Type == (int)eObjectType.Shield) return false;
                if (style.Spec == Specs.HandToHand)
                    return primary.Object_Type == (int)eObjectType.HandToHand && left.Object_Type == (int)eObjectType.HandToHand;
                if (style.Spec == Specs.Left_Axe) return left.Object_Type is (int)eObjectType.LeftAxe or (int)eObjectType.Axe;
                return GlobalConstants.IsWeapon(left.Object_Type);
            }
            if (style.WeaponTypeRequirement == Style.SpecialWeaponType.AnyWeapon)
                return GlobalConstants.IsWeapon(primary.Object_Type) && primary.Object_Type != (int)eObjectType.Shield;
            if (style.WeaponTypeRequirement == (int)eObjectType.Shield)
                return slot == eActiveWeaponSlot.Standard && left?.Object_Type == (int)eObjectType.Shield;
            eObjectType type = primary.Object_Type == (int)eObjectType.LeftAxe ? eObjectType.Axe : (eObjectType)primary.Object_Type;
            return GameServer.ServerRules.IsObjectTypesEqual((eObjectType)style.WeaponTypeRequirement, type);
        }

        public static Style Select(GameBot bot, AttackData lastAttack, bool preserveQueued = true)
        {
            if (bot?.Styles == null || !BotWeaponStats.FitsConfiguredSlot(bot, bot.ActiveWeapon,
                    bot.ActiveWeaponSlot == eActiveWeaponSlot.TwoHanded ? eInventorySlot.TwoHandWeapon : eInventorySlot.RightHandWeapon) ||
                bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance)
                return null;
            // Preserve a deliberate tank taunt, but revalidate at the actual swing.
            bool mayTaunt = MayTaunt(bot);
            Style queued = bot.styleComponent.NextCombatStyle;
            GameLiving target = bot.TargetObject as GameLiving;
            if (preserveQueued && Usable(bot, queued, lastAttack) && (mayTaunt || ThreatSign(queued) <= 0) &&
                (!IsStunStyle(queued) || StunWanted(bot, target, lastAttack))) return queued;
            bool detaunt = ShouldDetaunt(bot);
            Style best = null;
            double bestValue = 0;
            Style detauntStyle = null;
            Style anytime = null;
            Style stealthOpener = null;
            bool? stunWanted = null;
            foreach (Style style in bot.Styles)
            {
                if (!Usable(bot, style, lastAttack)) continue;
                // hidden and in place: the stealth opener comes first, whatever its growth (its damage is its own formula)
                if (style.StealthRequirement)
                {
                    if (stealthOpener == null || style.Level > stealthOpener.Level) stealthOpener = style;
                    continue;
                }
                if (IsStunStyle(style) && !(stunWanted ??= StunWanted(bot, target, lastAttack))) continue;
                int threat = ThreatSign(style);
                if (threat > 0 && !mayTaunt) continue;
                if (threat < 0 && detaunt && Better(style, detauntStyle)) detauntStyle = style;
                double value = Value(bot, style, mayTaunt);
                if (best == null || value > bestValue || value == bestValue && style.Level > best.Level)
                {
                    best = style;
                    bestValue = value;
                }
                if (SavageBotCombatPolicy.IsReliableAnytimeStyle(style) && Better(style, anytime)) anytime = style;
            }
            bot.styleComponent.NextCombatBackupStyle = anytime;
            return stealthOpener ?? detauntStyle ?? best;
        }
    }
}
