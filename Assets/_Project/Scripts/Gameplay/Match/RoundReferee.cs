using Snackdown.Gameplay.Player;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// Decides when a round is over and who won it.
    /// </summary>
    /// <remarks>
    /// <para>Separate from <see cref="MatchDirector"/>, which owns the phase and the arena but
    /// deliberately knows nothing about winning. The two change for different reasons: adding a
    /// game mode rewrites this and leaves scene loading alone, and adding an arena rewrites that
    /// and leaves the rules alone.</para>
    /// <para>Server-only, like everything that decides an outcome. The result is replicated as a
    /// verdict — winner and reason — rather than as the ingredients, so a client cannot reach a
    /// different conclusion from the same numbers and disagree about who won.</para>
    /// </remarks>
    public class RoundReferee : NetworkBehaviour
    {
        [Tooltip("Match rules. Should be the same asset the players' life uses.")]
        [SerializeField] MatchConfig _config;

        /// <summary>Sentinel for a match with no winner, since client ids start at zero.</summary>
        public const ulong NoWinnerId = ulong.MaxValue;

        readonly NetworkVariable<ulong> _winner = new NetworkVariable<ulong>(NoWinnerId);
        readonly NetworkVariable<MatchOutcome> _outcome = new NetworkVariable<MatchOutcome>(MatchOutcome.Undecided);

        /// <summary>
        /// Server time at which the round is over, published once instead of counted down.
        /// </summary>
        /// <remarks>
        /// The same deadline-not-a-counter reasoning as the start countdown, and for the same
        /// reason: a timer replicated every frame is bandwidth spent on a number every peer could
        /// have derived from a clock they already share.
        /// </remarks>
        readonly NetworkVariable<double> _roundEndsAtServerTime = new NetworkVariable<double>(0d);

        /// <summary>How many players the round began with. Server-side only.</summary>
        int _startingPlayers;

        /// <summary>
        /// Phase this referee has already reacted to, so the start of a round is noticed once.
        /// </summary>
        /// <remarks>
        /// Compared each frame rather than subscribed to. Both this and the director spawn as
        /// network objects with no guaranteed order between them, so a subscription taken in
        /// <c>OnNetworkSpawn</c> is a subscription that silently does nothing whenever the director
        /// happens to arrive second.
        /// </remarks>
        MatchPhase _reactedTo = MatchPhase.Lobby;

        public ulong WinnerClientId => _winner.Value;
        public MatchOutcome Outcome => _outcome.Value;
        public bool HasWinner => _winner.Value != NoWinnerId;

        /// <summary>Seconds left in the round, derived locally from the shared clock.</summary>
        public float RoundRemaining
        {
            get
            {
                if (_roundEndsAtServerTime.Value <= 0d || NetworkManager == null) return 0f;
                return Mathf.Max(0f, (float)(_roundEndsAtServerTime.Value - NetworkManager.ServerTime.Time));
            }
        }

        public static RoundReferee Current { get; private set; }

        public override void OnNetworkSpawn()
        {
            Current = this;

            if (IsServer && _config == null)
                Debug.LogWarning($"[Snackdown] {name} has no MatchConfig; falling back to a {FallbackRoundSeconds}s round.", this);
        }

        public override void OnNetworkDespawn()
        {
            if (ReferenceEquals(Current, this)) Current = null;
        }

        /// <summary>Round length used when no config is assigned, so a match still ends.</summary>
        const float FallbackRoundSeconds = 180f;

        void Update()
        {
            if (!IsServer) return;

            MatchDirector director = MatchDirector.Current;
            if (director == null) return;

            if (director.Phase != _reactedTo)
            {
                _reactedTo = director.Phase;
                if (_reactedTo == MatchPhase.Playing) BeginRound();
            }

            if (!director.IsPlaying) return;

            if (NetworkManager.ServerTime.Time >= _roundEndsAtServerTime.Value)
            {
                Decide(BestSurvivor(), MatchOutcome.TimeUp);
                return;
            }

            int alive = CountAlive();

            if (alive == 0)
            {
                Decide(NoWinnerId, MatchOutcome.NoWinner);
                return;
            }

            // A solo session has one player and nobody to outlast, so being the only one alive is
            // the starting condition rather than a victory. Without this the match would end on the
            // frame it began, which is exactly what happens when someone tests alone.
            if (alive == 1 && _startingPlayers >= 2)
                Decide(LastSurvivor(), MatchOutcome.LastStanding);
        }

        void BeginRound()
        {
            _winner.Value = NoWinnerId;
            _outcome.Value = MatchOutcome.Undecided;
            _startingPlayers = PlayerLife.All.Count;

            float seconds = _config != null ? _config.RoundSeconds : FallbackRoundSeconds;
            _roundEndsAtServerTime.Value = NetworkManager.ServerTime.Time + seconds;
        }

        void Decide(ulong winner, MatchOutcome outcome)
        {
            _winner.Value = winner;

            // No winner is a result, not a missing one: two players going out on the same tick, or
            // finishing level on the clock, is an ending the end screen has to be able to state.
            _outcome.Value = winner == NoWinnerId ? MatchOutcome.NoWinner : outcome;

            MatchDirector.Current.ServerEndMatch();
        }

        static int CountAlive()
        {
            int alive = 0;

            for (int i = 0; i < PlayerLife.All.Count; i++)
                if (PlayerLife.All[i].IsAlive) alive++;

            return alive;
        }

        static ulong LastSurvivor()
        {
            for (int i = 0; i < PlayerLife.All.Count; i++)
                if (PlayerLife.All[i].IsAlive) return PlayerLife.All[i].OwnerClientId;

            return NoWinnerId;
        }

        /// <summary>
        /// Whoever has the most life left when the clock runs out.
        /// </summary>
        /// <remarks>
        /// A shared top value is reported as no winner rather than broken by client id or spawn
        /// order. Life is capped by <see cref="MatchConfig.MaxLife"/>, so two players sitting at the
        /// ceiling is a reachable state and not a floating-point curiosity — resolving it by an
        /// arbitrary rule would hand someone a win for having connected first.
        /// </remarks>
        static ulong BestSurvivor()
        {
            ulong best = NoWinnerId;
            float bestLife = 0f;
            bool tied = false;

            for (int i = 0; i < PlayerLife.All.Count; i++)
            {
                PlayerLife life = PlayerLife.All[i];
                if (!life.IsAlive) continue;

                if (life.Remaining > bestLife)
                {
                    best = life.OwnerClientId;
                    bestLife = life.Remaining;
                    tied = false;
                }
                else if (best != NoWinnerId && Mathf.Approximately(life.Remaining, bestLife))
                {
                    tied = true;
                }
            }

            return tied ? NoWinnerId : best;
        }
    }
}
