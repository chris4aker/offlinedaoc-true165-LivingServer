namespace DOL.GS
{
    /// <summary>
    /// What one stat buff instance has put on its owner (owner 2026-11-10). A pulsing song or chant renewal hands the bonus to
    /// the next instance. The replaced instance then adds and removes nothing: two Bards (or Paladins) singing the same song
    /// swapped instances in one tick, and the singer's pulse later ended the replaced one, taking the bonus off a second time.
    /// Group endurance regeneration sat at 0, even sitting, and released bots never got full (36 bots, 5 hours).
    /// </summary>
    public sealed class StatBuffBonusLedger
    {
        public bool IsApplied { get; private set; }
        public bool IsSuperseded { get; private set; }
        public double AppliedEffectiveness { get; private set; }

        /// <summary>True when the bonus goes on now; records the effectiveness it goes on with.</summary>
        public bool BeginApply(double effectiveness)
        {
            if (IsApplied || IsSuperseded)
                return false;
            IsApplied = true;
            AppliedEffectiveness = effectiveness;
            return true;
        }

        /// <summary>True when the bonus that went on comes off now, at the effectiveness it went on with.</summary>
        public bool BeginRemove(out double effectiveness)
        {
            effectiveness = AppliedEffectiveness;
            if (!IsApplied)
                return false;
            IsApplied = false;
            return true;
        }

        /// <summary>This instance was replaced: it never applies again. True when its bonus was on and comes off now.</summary>
        public bool Supersede(out double effectiveness)
        {
            IsSuperseded = true;
            return BeginRemove(out effectiveness);
        }
    }
}
