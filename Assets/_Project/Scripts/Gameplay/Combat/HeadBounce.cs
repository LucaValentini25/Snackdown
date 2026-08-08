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

        void Update()
        {
            if (!IsServer) return;

            MatchDirector director = MatchDirector.Current;
            if (director == null || !director.IsPlaying) return;

            Collect();

            for (int i = 0; i < _candidates.Count; i++)
                for (int j = i + 1; j < _candidates.Count; j++)
                    Resolve(_candidates[i], _candidates[j]);
        }

        void Collect()
        {
            _candidates.Clear();

            foreach (IPredictedPeer peer in NetworkSimulationLoop.ActivePlayers)
                if (peer is PredictedPlayer player) _candidates.Add(player);
        }

        void Resolve(PredictedPlayer a, PredictedPlayer b)
        {
            // Whoever is higher is the one doing the stomping.
            PredictedPlayer upper = a.State.Position.y >= b.State.Position.y ? a : b;
            PredictedPlayer lower = ReferenceEquals(upper, a) ? b : a;

            if (!IsStacked(upper, lower)) return;

            // Must be coming down. Without this, two players jumping through each other on the way
            // up would stun whoever happened to be a pixel lower.
            if (upper.State.Velocity.y > -_minimumFallSpeed) return;

            // Already stunned: no chaining a second stun onto someone who cannot move anyway.
            if (lower.State.IsStunned) return;

            PlayerLife lowerLife = lower.GetComponent<PlayerLife>();
            if (lowerLife != null && !lowerLife.IsAlive) return;

            lower.ServerApplyStun(_stunSeconds);
            upper.ServerBounce(_bounceVelocity);
        }

        bool IsStacked(PredictedPlayer upper, PredictedPlayer lower)
        {
            Vector2 gap = upper.State.Position - lower.State.Position;
            return Mathf.Abs(gap.x) <= _horizontalReach && gap.y > 0f && gap.y <= _verticalReach;
        }
    }
}
