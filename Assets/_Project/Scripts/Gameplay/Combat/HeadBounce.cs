using System.Collections.Generic;
using Snackdown.Gameplay.Match;
using Snackdown.Gameplay.Player;
using Snackdown.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Combat
{
    /// <summary>
    /// Resolves players landing on each other's heads: the one on top bounces, the one below is
    /// stunned.
    /// </summary>
    /// <remarks>
    /// <para>Server-only, and checked once per tick over every pair rather than from each
    /// character. Two players landing on each other in the same tick is a real case — both would
    /// claim the stomp if each judged for itself — so the pairing is resolved in one place, in a
    /// fixed order, and each pair is considered exactly once.</para>
    /// <para>Deliberately outside <c>PlayerMotor</c>. The motor is a pure function of one
    /// character's own state and input, which is what lets a client replay it during
    /// reconciliation; player-versus-player contact depends on where <i>everyone else</i> was at
    /// that instant, which a replaying client does not know. So the server decides, and the result
    /// reaches clients as a stun timer inside the state they reconcile against — a correction they
    /// replay <b>through</b>, not one they have to predict.</para>
    /// </remarks>
    public class HeadBounce : NetworkBehaviour
    {
        [Tooltip("How long a stomped player loses control for.")]
        [SerializeField] float _stunSeconds = 2f;

        [Tooltip("Upward speed given to the player who stomps.")]
        [SerializeField] float _bounceVelocity = 12f;

        [Tooltip("How far apart two characters can be horizontally and still count as stacked.")]
        [SerializeField] float _horizontalReach = 0.6f;

        [Tooltip("Vertical gap between the two that counts as standing on a head.")]
        [SerializeField] float _verticalReach = 0.9f;

        [Tooltip("The stomper must be falling at least this fast. Stops a shared jump from counting.")]
        [SerializeField] float _minimumFallSpeed = 1f;

        readonly List<PredictedPlayer> _candidates = new List<PredictedPlayer>();

        public override void OnNetworkSpawn()
        {
            if (IsServer) NetworkSimulationLoop.AfterServerSimulation += ResolveAll;
        }

        public override void OnNetworkDespawn() => NetworkSimulationLoop.AfterServerSimulation -= ResolveAll;

        /// <remarks>
        /// Driven by the simulation tick rather than by <c>Update</c>. Players pass through each
        /// other — the motor only collides with static geometry, which is what keeps it replayable
        /// — so a falling character crosses another in roughly a single tick, and a check running
        /// on the render loop misses it whenever a frame straddles the crossing.
        /// </remarks>
        void ResolveAll(float tickDelta)
        {
            MatchDirector director = MatchDirector.Current;
            if (director == null || !director.IsPlaying) return;

            Collect();

            for (int i = 0; i < _candidates.Count; i++)
                for (int j = i + 1; j < _candidates.Count; j++)
                    Resolve(_candidates[i], _candidates[j], tickDelta);
        }

        void Collect()
        {
            _candidates.Clear();

            // Players who are out are skipped for the same reason they stop being solid: a stomp
            // landing on someone who has already lost is a stun nobody will ever see wear off.
            foreach (IPredictedPeer peer in NetworkSimulationLoop.ActivePlayers)
                if (peer is PredictedPlayer player && player.IsSolid) _candidates.Add(player);
        }

        void Resolve(PredictedPlayer a, PredictedPlayer b, float tickDelta)
        {
            // Whoever is higher is the one doing the stomping.
            PredictedPlayer upper = a.State.Position.y >= b.State.Position.y ? a : b;
            PredictedPlayer lower = ReferenceEquals(upper, a) ? b : a;

            if (!IsStacked(upper, lower, tickDelta)) return;

            // Must be coming down. Without this, two players jumping through each other on the way
            // up would stun whoever happened to be a pixel lower.
            if (upper.State.Velocity.y > -_minimumFallSpeed) return;

            // Already stunned: no chaining a second stun onto someone who cannot move anyway.
            if (lower.State.IsStunned) return;

            // The life left the avatar in ps-3, so being alive is a question about the player and
            // not about the object being stood on.
            PlayerSession lowerPlayer = PlayerSession.Of(NetworkManager, lower.OwnerClientId);
            if (lowerPlayer != null && lowerPlayer.Life != null && !lowerPlayer.Life.IsAlive) return;

            lower.ServerApplyStun(_stunSeconds);
            upper.ServerBounce(_bounceVelocity);
        }

        /// <remarks>
        /// The vertical window grows with how fast the stomper is falling. At terminal velocity a
        /// character covers most of its own height in one tick, so a fixed window would be stepped
        /// straight over: one tick above the head, the next already below it, contact never seen.
        /// Widening by the distance travelled this tick turns a snapshot test into a swept one.
        /// </remarks>
        bool IsStacked(PredictedPlayer upper, PredictedPlayer lower, float tickDelta)
        {
            Vector2 gap = upper.State.Position - lower.State.Position;
            if (Mathf.Abs(gap.x) > _horizontalReach) return false;

            float travelled = Mathf.Abs(upper.State.Velocity.y) * tickDelta;
            return gap.y > -travelled && gap.y <= _verticalReach + travelled;
        }
    }
}
