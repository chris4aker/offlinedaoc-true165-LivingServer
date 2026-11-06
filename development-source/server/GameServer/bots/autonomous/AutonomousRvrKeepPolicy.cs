using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>Portal keeps are protected travel hubs, never siege objectives.
    /// Relic keeps remain valid raid objectives even though the relic, rather
    /// than the keep itself, is the capturable objective.</summary>
    public static class AutonomousRvrKeepPolicy
    {
        public static bool IsSiegeObjective(AbstractGameKeep keep)
        {
            // Battleground central keeps start neutral (realm none) and are taken from that state.
            return keep != null && !keep.IsPortalKeep &&
                   (keep.Realm != eRealm.None || BattlegroundBrackets.IsBattlegroundRegion(keep.Region));
        }

        /// <summary>The hastener at a keep's gate (1.52b) is a peaceful helper, not a defender: it adds nothing to a
        /// keep's guard strength and no defending bot takes its spot outside the gate.</summary>
        public static bool IsDefendingGuard(GameKeepGuard guard) => guard != null && IsDefendingGuardType(guard.GetType());

        public static bool IsDefendingGuardType(System.Type type) => !typeof(FrontierHastener).IsAssignableFrom(type);
    }
}
