using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Which melee set a bot fights with: two-hander or weapon (and shield / off-hand) (owner 2026-11-05: "I'm not sure I
    /// ever saw my Armsman tank actually using Guard"; melee bots can carry a two-hander and a one-hander with a shield).
    /// One rule for the brain and the attack start, in this order:
    /// 1. a hired companion keeps the set its player readied last (the companion window), as long as that set is usable;
    /// 2. the group's only tank fights with weapon and shield when it has Guard (Prima: the defensive Armsman, Warrior,
    ///    Hero, Thane tank and guard with a shield; the offensive one, with another tank present, takes the two-hander);
    /// 3. otherwise its build (BotSpec.Is2H) and class as before.
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
            if (bot?.PlayerReadiedSlot is eActiveWeaponSlot readied && readied != eActiveWeaponSlot.Distance)
                return readied == eActiveWeaponSlot.TwoHanded;
            if (GuardDuty(bot))
                return false;
            return twoHandedBuild;
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
