using System.Numerics;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// owner 2026-11-10: a bot that plans the same zone crossing back and forth (A>B, then B>A, then A>B) with the same
    /// destination is stuck on ground that does not lead on. Each 128-unit hop counted as route progress, so Albion bots
    /// turned back and forth on the Camelot Hills / Salisbury / Black Mtns. South corner until the 45-minute watchdog.
    /// Pure rules; the controller fails such a plan the same way as a missing seam.
    /// </summary>
    public static class AutonomousSeamReversalGuard
    {
        [ServerProperty("bots", "bot_seam_reversal_guard",
            "Do bots give up a route that turns back and forth over the same zone crossing (same destination, within a minute) instead of walking it until the 45-minute watchdog?", true)]
        public static bool Enabled = true;

        [ServerProperty("bots", "bot_seam_reversal_forget_step",
            "When the seam reversal guard catches a crossing, is it dropped from the shared zone-step memory (and remembered as failed for 90 s) so other bots are not handed the same crossing?", true)]
        public static bool ForgetSteps = true;

        /// <summary>How close the reversed crossing's points must be (XY) to count as the same crossing.</summary>
        public const float Tolerance = 64f;
        /// <summary>A destination moved more than this is a new route (same as the controller's destination change).</summary>
        public const float DestinationTolerance = 96f;
        public const long WindowMilliseconds = 60_000;
        /// <summary>A destination this close to the crossing may cross back and forth (the goal sits on the border).</summary>
        public const float ExemptRadius = 512f;
        /// <summary>A>B, B>A, A>B: two reversals in a row.</summary>
        public const int TriggerReversals = 2;

        public struct State
        {
            public AutonomousZoneBoundaryRouting.Step? Last;
            public Vector3 Destination;
            public long Tick;
            public int Reversals;
        }

        /// <summary>True when next walks back over previous: next starts where previous ended and ends where it started.</summary>
        public static bool IsReversal(AutonomousZoneBoundaryRouting.Step previous, AutonomousZoneBoundaryRouting.Step next,
            float tolerance = Tolerance)
        {
            float limit = tolerance * tolerance;
            return Vector2.DistanceSquared(Flat(previous.Outside), Flat(next.Inside)) <= limit &&
                   Vector2.DistanceSquared(Flat(previous.Inside), Flat(next.Outside)) <= limit;
        }

        /// <summary>
        /// Notes a newly planned crossing and returns the reversals in a row. Back to 0 when the destination moved, the last
        /// crossing is older than the window, or this crossing does not reverse the last one.
        /// </summary>
        public static int Note(ref State state, AutonomousZoneBoundaryRouting.Step step, Vector3 destination, long now)
        {
            bool reversed = state.Last is { } last &&
                            Vector3.DistanceSquared(state.Destination, destination) <= DestinationTolerance * DestinationTolerance &&
                            now - state.Tick <= WindowMilliseconds &&
                            IsReversal(last, step);
            state.Reversals = reversed ? state.Reversals + 1 : 0;
            state.Last = step;
            state.Destination = destination;
            state.Tick = now;
            return state.Reversals;
        }

        /// <summary>True when the destination is within <see cref="ExemptRadius"/> of either side of the crossing.</summary>
        public static bool Exempt(AutonomousZoneBoundaryRouting.Step step, Vector3 destination)
        {
            float limit = ExemptRadius * ExemptRadius;
            return Vector2.DistanceSquared(Flat(step.Inside), Flat(destination)) <= limit ||
                   Vector2.DistanceSquared(Flat(step.Outside), Flat(destination)) <= limit;
        }

        /// <summary>Whether this newly planned crossing should fail the route.</summary>
        public static bool ShouldFail(ref State state, AutonomousZoneBoundaryRouting.Step step, Vector3 destination, long now) =>
            Note(ref state, step, destination, now) >= TriggerReversals && !Exempt(step, destination);

        /// <summary>
        /// owner 2026-11-10 (review): a non-leader member of a dynamic group fails only its own step and holds (the group
        /// keeps its shared camp); a leader, a solo bot or a realm-raid member runs the full no-seam chain.
        /// </summary>
        public static bool FailsMemberOnly(bool dynamicGroup, bool isLeader, bool inRealmRaid) =>
            dynamicGroup && !isLeader && !inRealmRaid;

        /// <summary>
        /// How long a member holds after its step failed before it plans again (a moved destination re-plans at once).
        /// Under the controller's 12 s route-stall time, so a holding member never escalates into stall recovery.
        /// </summary>
        public const long MemberHoldMilliseconds = 10_000;

        private static Vector2 Flat(Vector3 point) => new(point.X, point.Y);
    }
}
