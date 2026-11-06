using System;
using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// How a bot spends its spec points on its build plan. Pure, so tests can level a plan from 1 to 50.
    /// </summary>
    public static class BotSpecTrainer
    {
        /// <summary>Points a level brings, as GamePlayer grants them.</summary>
        public static int PointsForLevel(int multiplier, int level)
        {
            int points = 0;

            // The half level before it (40.5 to 49.5). Players get these at every half level; bots get them with the
            // next level, the same way whether they train every level or catch up several at once.
            if (level > 40)
                points += multiplier * (level - 1) / 20;

            if (level > 5)
                points += multiplier * level / 10;
            else if (level >= 2)
                points = level;

            return points;
        }

        /// <summary>
        /// Spends the points of levels <paramref name="fromLevel"/> to <paramref name="toLevel"/> on the plan's lines
        /// (already in ratio order, highest first) and returns the points left over. Each level every line is first
        /// bought up to its ratio of that level, then what is left buys lines up to the level in plan order.
        /// A line with ratio 0 waits for level 50 (the "rest of the points" lines).
        /// </summary>
        public static int Spend(IReadOnlyList<BotSpecLine> lines, int fromLevel, int toLevel, int botLevel, int leftOver,
            int multiplier, Func<string, int> getLevel, Action<string, int> setLevel)
        {
            for (int i = fromLevel; i <= toLevel; i++)
            {
                bool spentPoints = true;
                bool spendLeftOverPoints = false;
                int points = PointsForLevel(multiplier, i) + leftOver;

                while (spentPoints)
                {
                    spentPoints = false;

                    foreach (BotSpecLine specLine in lines)
                    {
                        if (specLine.levelRatio <= 0 && botLevel < 50)
                            continue;

                        int level = getLevel(specLine.Spec);
                        if (level < 0 || level >= specLine.SpecCap || level >= i)
                            continue;

                        if (!spendLeftOverPoints && level >= (int)(i * specLine.levelRatio))
                            continue;

                        int cost = level + 1;
                        if (points >= cost)
                        {
                            points -= cost;
                            setLevel(specLine.Spec, level + 1);
                            spentPoints = true;
                        }
                    }

                    if (!spentPoints && !spendLeftOverPoints)
                    {
                        spendLeftOverPoints = true;
                        spentPoints = true;
                    }
                }

                leftOver = points;
            }

            return leftOver;
        }
    }
}
