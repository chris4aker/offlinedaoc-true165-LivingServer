using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Which melee set a bot fights with: two-hander or weapon (and shield / off-hand) (owner 2026-11-05: "I'm not sure I
    /// ever saw my Armsman tank actually using Guard"; melee bots can carry a two-hander and a one-hander with a shield).
    /// One rule for the brain and the attack start, in this order:
    /// 1. a hired companion keeps the set its player readied last (the companion window), as long as that set is usable,
    ///    but never a two-hander it is trained in less than its one-hander (UntrainedTwoHander);
    /// 2. the group's only tank fights with weapon and shield when it has Guard (Prima: the defensive Armsman, Warrior,
    ///    Hero, Thane tank and guard with a shield; the offensive one, with another tank present, takes the two-hander);
    /// 3. the set whose weapon line it has trained higher (owner 2026-11-07: Osbert, Two Handed 9 against 1 in every
    ///    one-hand line, went back to sword and shield after a relog - the readied choice is not saved and his
    ///    generation-time build said sword and shield). Both sets stay equipped; a tie leaves it to
    /// 4. its build (BotSpec.Is2H) and class as before.
    /// A set that is not usable is never chosen.
    /// </summary>
    public static class BotWeaponSetPolicy
    {
        public static bool PreferTwoHanded(GameBot bot, bool twoHandUsable, bool standardUsable, bool twoHandedBuild)
        {
            if (!twoHandUsable)
                return false;
            if (!standardUsable)
                return true;
            if (UntrainedTwoHander(bot))
                return false;
            if (bot?.PlayerReadiedSlot is eActiveWeaponSlot readied && readied != eActiveWeaponSlot.Distance)
                return readied == eActiveWeaponSlot.TwoHanded;
            if (GuardDuty(bot))
                return false;
            if (TrainedSet(bot) is bool trainedTwoHand)
                return trainedTwoHand;
            return twoHandedBuild;
        }

        /// <summary>
        /// The set the bot is trained in more: true for the two-hander, false for the right-hand weapon, null with no
        /// opinion (one set missing, a dual-wield set, specializations not loaded, or the same line trained alike - a
        /// Midgard two-handed and one-handed sword share Sword). Shields does not count: it is a defensive line.
        /// </summary>
        public static bool? TrainedSet(GameBot bot)
        {
            if (bot?.Inventory == null)
                return null;
            DbInventoryItem twoHand = bot.Inventory.GetItem(eInventorySlot.TwoHandWeapon);
            DbInventoryItem rightHand = bot.Inventory.GetItem(eInventorySlot.RightHandWeapon);
            DbInventoryItem leftHand = bot.Inventory.GetItem(eInventorySlot.LeftHandWeapon);
            if (twoHand == null || rightHand == null)
                return null;
            return TrainedSet(bot.TrainedWeaponSpec((eObjectType)twoHand.Object_Type, twoHand.SlotPosition),
                bot.TrainedWeaponSpec((eObjectType)rightHand.Object_Type, rightHand.SlotPosition),
                leftHand != null && leftHand.Object_Type != (int)eObjectType.Shield);
        }

        /// <summary>The trained-set rule on its numbers (see <see cref="TrainedSet(GameBot)"/>).</summary>
        public static bool? TrainedSet(int twoHandSpec, int rightHandSpec, bool dualWieldSet) =>
            dualWieldSet || twoHandSpec == rightHandSpec ? null : twoHandSpec > rightHandSpec;

        /// <summary>
        /// A hired companion fights with the weapon it is trained in (owner 2026-11-06: an Armsman trained in slash, shield
        /// and parry was given a pike and stopped using his sword and shield). A two-hander whose line it has trained less
        /// than its right-hand weapon's stays unused while the weapon set is usable; training the line puts it to use.
        /// </summary>
        public static bool UntrainedTwoHander(GameBot bot)
        {
            if (bot?.Inventory == null || !HiredCompanions165.IsHired(bot))
                return false;
            DbInventoryItem twoHand = bot.Inventory.GetItem(eInventorySlot.TwoHandWeapon);
            DbInventoryItem rightHand = bot.Inventory.GetItem(eInventorySlot.RightHandWeapon);
            if (twoHand == null || rightHand == null)
                return false;
            return bot.TrainedWeaponSpec((eObjectType)twoHand.Object_Type, twoHand.SlotPosition) <
                   bot.TrainedWeaponSpec((eObjectType)rightHand.Object_Type, rightHand.SlotPosition);
        }

        /// <summary>Grouped, a tank class with Guard, a shield in the left hand, and no other living tank in the group.</summary>
        public static bool GuardDuty(GameBot bot)
        {
            if (!Classic165Mode.IsOn || bot?.Group == null || bot.Group.MemberCount < 2 || !BotPartyRoles.IsTank(bot) ||
                !bot.HasAbility(Abilities.Guard) ||
                bot.Inventory?.GetItem(eInventorySlot.LeftHandWeapon) is not DbInventoryItem { Object_Type: (int)eObjectType.Shield })
                return false;
            foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
            {
                if (member == null || member == bot || !member.IsAlive)
                    continue;
                if (member is GameBot other ? BotPartyRoles.IsTank(other) :
                    member is GamePlayer { CharacterClass: not null } player &&
                    BotPartyRoles.For((eCharacterClass)player.CharacterClass.ID) == BotPartyRole.Tank)
                    return false;
            }
            return true;
        }
    }
}
