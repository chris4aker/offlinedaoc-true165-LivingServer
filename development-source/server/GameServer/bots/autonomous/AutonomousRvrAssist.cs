using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DOL.GS;

/// <summary>
/// True 1.65 Mode warband assist: the main assist (the warband leader) calls one target and the members /assist it,
/// as period groups fought. Kill order: healers and support singers, then cloth casters, then archers, then the rest
/// (tanks, other melee, pets and engines). One frontier scan per warband instead of one per member.
/// </summary>
public static class AutonomousRvrAssist
{
    // A call lasts a few seconds past the leader's last refresh; a dead or invalid target ends it at once.
    public const long CallMilliseconds = 6_000;

    private sealed class Call
    {
        public GameLiving Target;
        public long Until;
    }

    // Keyed by the Group object: a disbanded group's entry goes with it.
    private static readonly ConditionalWeakTable<object, Call> Calls = new();

    public static bool Applies(bool classicMode, bool rvrWarband, bool defending, bool siegeFighter, bool siegeAssigned) =>
        classicMode && rvrWarband && !defending && !siegeFighter && !siegeAssigned;

    public static void CallTarget(object group, GameLiving target, long now)
    {
        if (group == null || target == null) return;
        Call call = Calls.GetOrCreateValue(group);
        lock (call)
        {
            call.Target = target;
            call.Until = now + CallMilliseconds;
        }
    }

    public static GameLiving CalledTarget(object group, long now)
    {
        if (group == null || !Calls.TryGetValue(group, out Call call)) return null;
        lock (call)
        {
            if (call.Until <= now || call.Target is not { IsAlive: true, ObjectState: GameObject.eObjectState.Active })
                return null;
            return call.Target;
        }
    }

    /// <summary>0 healers/support, 1 cloth casters, 2 archers, 3 other classes, 4 anything without a class (pets, engines).</summary>
    public static int KillOrderRank(eCharacterClass? characterClass)
    {
        if (characterClass is not { } c || c == eCharacterClass.Unknown) return 4;
        if (BotPartyRoles.IsHealingClass(c) && BotPartyRoles.For(c) != BotPartyRole.Tank ||
            c is eCharacterClass.Bard or eCharacterClass.Skald or eCharacterClass.Minstrel)
            return 0;
        if (IsClothCaster(c)) return 1;
        if (BotRangedCombat.IsDedicatedArcher(c)) return 2;
        return 3;
    }

    public static bool IsClothCaster(eCharacterClass c) => c is
        eCharacterClass.Wizard or eCharacterClass.Sorcerer or eCharacterClass.Theurgist or eCharacterClass.Cabalist or
        eCharacterClass.Necromancer or eCharacterClass.Mage or eCharacterClass.Elementalist or
        eCharacterClass.Runemaster or eCharacterClass.Spiritmaster or eCharacterClass.Bonedancer or eCharacterClass.Mystic or
        eCharacterClass.Eldritch or eCharacterClass.Enchanter or eCharacterClass.Mentalist or eCharacterClass.Animist or
        eCharacterClass.Magician or eCharacterClass.Disciple;

    public static eCharacterClass? ClassOf(GameLiving living) => living switch
    {
        GamePlayer player when player.CharacterClass != null => (eCharacterClass)player.CharacterClass.ID,
        GameBot bot when bot.CharacterClass != null => (eCharacterClass)bot.CharacterClass.ID,
        _ => null,
    };

    /// <summary>
    /// The main assist's pick: an enemy hitting a friendly siege operator first, then the relic carrier, then kill
    /// order, then the nearest. Null when there are no candidates.
    /// </summary>
    public static T Pick<T>(IReadOnlyList<T> candidates, Func<T, bool> operatorThreat, Func<T, bool> relicCarrier,
        Func<T, int> rank, Func<T, double> distance) where T : class
    {
        T best = null;
        (int, int, int, double) bestKey = default;
        foreach (T candidate in candidates)
        {
            var key = (operatorThreat(candidate) ? 0 : 1, relicCarrier(candidate) ? 0 : 1, rank(candidate), distance(candidate));
            if (best == null || key.CompareTo(bestKey) < 0)
            {
                best = candidate;
                bestKey = key;
            }
        }
        return best;
    }
}
