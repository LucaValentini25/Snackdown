namespace Snackdown.Netcode
{
    /// <summary>
    /// Where a client gets the other players' positions from when it predicts contact with them.
    /// </summary>
    /// <remarks>
    /// <para>Two ways of being wrong, and the project has never measured which is less wrong. A
    /// client cannot know where a rival is <i>now</i> — it only has where the rival was when the
    /// last snapshot left the server. Every option is a way of guessing across that gap.</para>
    /// <para>Both fill the same <see cref="WorldSnapshotBuffer"/>, so prediction and replay always
    /// read the same numbers and a correction never comes from a replay disagreeing with itself.
    /// What changes is only how far those numbers sit from what the server computed.</para>
    /// <para>A switch rather than a decision, deliberately. The roadmap has held this open since
    /// Phase 1 on the grounds that choosing well means feeling the difference; feeling it means
    /// running both, and running both means being able to change it without a rebuild. The two
    /// are measured by the procedure in <c>docs/05</c> and the answer belongs there.</para>
    /// </remarks>
    public enum PeerContactSource
    {
        /// <summary>
        /// Where the rival is being drawn: the interpolator's output, about 100 ms behind server
        /// time, filed under a prediction tick that runs ahead of the server by the round trip.
        /// </summary>
        /// <remarks>
        /// The offset is therefore <c>interpolation delay + client lead</c> — a fifth of a second at
        /// a normal round trip, during which a running player covers more than their own width. It
        /// predicts contact against what the player can see, which is the case for it: the collision
        /// you predicted is the one that looked like it was going to happen.
        /// </remarks>
        Interpolated = 0,

        /// <summary>
        /// The newest state the server actually sent for that rival, carried forward at the velocity
        /// it was moving at to reach the tick being predicted.
        /// </summary>
        /// <remarks>
        /// Trades a known offset for an extrapolation error. It is right whenever the rival keeps
        /// doing what they were doing and wrong exactly when they stop, turn or land — and it
        /// predicts contact against a position nothing on this machine is drawing, so a rejected
        /// prediction is one the player watched succeed.
        /// </remarks>
        Authoritative = 1
    }
}
