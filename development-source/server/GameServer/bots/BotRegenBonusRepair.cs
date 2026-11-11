using System.Text;

namespace DOL.GS
{
    /// <summary>
    /// Safety net after a bot's death (owner 2026-11-10). Death ends every buff, so a regeneration buff bonus left on a
    /// living bot with no regeneration buff effect is drift. A negative one kept endurance at 0% even sitting and held
    /// released bots in "Recovering after release" for hours. Runs once per observed death; one log line when it repairs.
    /// </summary>
    public static class BotRegenBonusRepair
    {
        private static readonly (eEffect Effect, eProperty Property, string Name)[] RegenBuffs =
        [
            (eEffect.HealthRegenBuff, eProperty.HealthRegenerationAmount, "health"),
            (eEffect.PowerRegenBuff, eProperty.PowerRegenerationAmount, "power"),
            (eEffect.EnduranceRegenBuff, eProperty.EnduranceRegenerationAmount, "endurance"),
        ];

        /// <summary>A regeneration buff bonus is drift when no buff effect of that kind is on the bot.</summary>
        public static bool IsDrift(int bonus, bool buffEffectPresent) => bonus != 0 && !buffEffectPresent;

        /// <summary>Clears drifted regeneration buff bonuses; returns what was repaired ("endurance:-6"), or empty.</summary>
        public static string Repair(GameLiving living)
        {
            if (living == null || !living.IsAlive || living.effectListComponent == null)
                return string.Empty;
            StringBuilder repaired = null;
            foreach ((eEffect effect, eProperty property, string name) in RegenBuffs)
            {
                int bonus = living.BaseBuffBonusCategory[property];
                if (!IsDrift(bonus, living.effectListComponent.ContainsEffectForEffectType(effect)))
                    continue;
                living.BaseBuffBonusCategory[property] = 0;
                (repaired ??= new()).Append(repaired.Length > 0 ? "," : string.Empty).Append(name).Append(':').Append(bonus);
            }
            if (repaired == null)
                return string.Empty;
            living.StartHealthRegeneration();
            living.StartPowerRegeneration();
            living.StartEnduranceRegeneration();
            return repaired.ToString();
        }
    }
}
