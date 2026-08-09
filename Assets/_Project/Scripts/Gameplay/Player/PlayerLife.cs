using System;
using System.Collections.Generic;
using Snackdown.Gameplay.Match;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Player
{
    /// <summary>
    /// A player's life, which is also their clock: it drains on its own, fruit tops it up, and
    /// running out is how you lose.
    /// </summary>
    /// <remarks>
    /// <para>The server owns the number and everyone else reads it. That is not ceremony — life
    /// decides who wins, and a client that could write its own would simply never lose.</para>
    /// <para>Drained continuously but <b>replicated on an interval</b>. The original subtracted
    /// <c>Time.deltaTime</c> from a <c>NetworkVariable</c> every frame, which meant sixty writes a
    /// second per player for a value displayed as whole seconds. Here the authoritative number
    /// advances every frame server-side and is published about once a second; clients interpolate
    /// downward between updates, so the countdown reads smoothly while costing a sixtieth of the
    /// bandwidth.</para>
    /// </remarks>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerLife : NetworkBehaviour
    {
        [SerializeField] MatchConfig _config;

        /// <summary>Authoritative life, published on an interval rather than every frame.</summary>
        readonly NetworkVariable<float> _life = new NetworkVariable<float>(0f);

        /// <summary>Server-side running value, exact and updated every frame.</summary>
        float _exactLife;
        float _sinceLastPublish;

        /// <summary>Client-side value that slides between published updates.</summary>
        float _displayed;

        /// <summary>Whether this player is still in the round.</summary>
        /// <remarks>
        /// Replicated as its own flag rather than derived from <see cref="Remaining"/>, because the
        /// number clients hold is interpolated: it drains locally between updates and can reach
        /// zero a fraction of a second before the server agrees. Deriving death from it would let a
        /// client bury a player who is still alive — and stop drawing them, stop colliding with
        /// them, and hand its owner a spectator camera — over a rounding difference. A flag the
        /// server sets is one answer everyone shares.
        /// </remarks>
        readonly NetworkVariable<bool> _alive = new NetworkVariable<bool>(true);

        public bool IsAlive => _alive.Value;

        /// <summary>Seconds left, smoothed on clients so the readout does not tick in steps.</summary>
        public float Remaining => IsServer ? _exactLife : _displayed;

        /// <summary>Fraction of <see cref="MatchConfig.MaxLife"/> remaining, for a bar.</summary>
        public float Fraction => _config != null && _config.MaxLife > 0f
            ? Mathf.Clamp01(Remaining / _config.MaxLife)
            : 0f;

        /// <summary>Raised on the server the moment this player runs out.</summary>
        public event Action<PlayerLife> Died;

        /// <summary>Raised on every peer when this player's alive flag arrives or changes.</summary>
        /// <remarks>
        /// Separate from <see cref="Died"/> on purpose: that one is the server's decision, this one
        /// is everybody's notification, and they do not happen at the same moment or on the same
        /// machines.
        /// </remarks>
        public event Action<PlayerLife> AliveChanged;

        bool _reportedDeath;

        static readonly List<PlayerLife> _all = new List<PlayerLife>();

        /// <summary>Every player currently in the session, in no particular order.</summary>
        /// <remarks>
        /// A registry kept by spawn and despawn rather than a scene search. Deciding a round means
        /// asking "who is left" on the server every frame, and <c>FindObjectsByType</c> at that
        /// rate is the kind of cost that does not show up until a profiler is opened. It also
        /// removes a whole class of bug: a player who disconnects mid-match leaves this list at
        /// despawn, so they cannot go on counting as alive.
        /// </remarks>
        public static IReadOnlyList<PlayerLife> All => _all;

        public override void OnNetworkSpawn()
        {
            if (_config == null)
            {
                Debug.LogError($"[Snackdown] {name} has no MatchConfig; life will not run.", this);
                enabled = false;
                return;
            }

            _all.Add(this);

            _life.OnValueChanged += OnLifePublished;
            _alive.OnValueChanged += OnAlivePublished;

            if (IsServer)
            {
                _exactLife = _config.StartingLife;
                _life.Value = _exactLife;
                _alive.Value = true;
            }

            _displayed = _life.Value;

            // Announced on spawn as well as on change, so anything watching starts from the truth
            // instead of from whatever it assumed before this player existed.
            AliveChanged?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            _all.Remove(this);

            _life.OnValueChanged -= OnLifePublished;
            _alive.OnValueChanged -= OnAlivePublished;
        }

        void OnAlivePublished(bool previous, bool current) => AliveChanged?.Invoke(this);

        void OnLifePublished(float previous, float current)
        {
            // A jump upward is fruit, and should read as a jump. A jump downward is just the
            // interval catching up with a drain the client was already predicting, so it slides.
            if (current > _displayed) _displayed = current;
        }

        void Update()
        {
            if (IsServer) ServerTick();
            else ClientTick();
        }

        void ServerTick()
        {
            MatchDirector director = MatchDirector.Current;
            if (director == null || !director.IsPlaying) return;
            if (!IsAlive) return;

            _exactLife = Mathf.Max(0f, _exactLife - _config.DrainPerSecond * Time.deltaTime);

            _sinceLastPublish += Time.deltaTime;

            // Death is published immediately rather than waiting for the next interval: it decides
            // the round, and up to a second of everyone believing a dead player is alive is a
            // second of shots landing on someone who has already lost.
            bool ranOut = _exactLife <= 0f;
            if (_sinceLastPublish >= _config.ReplicationInterval || ranOut)
            {
                _sinceLastPublish = 0f;
                _life.Value = _exactLife;
            }

            if (ranOut && !_reportedDeath)
            {
                _reportedDeath = true;
                _alive.Value = false;
                Died?.Invoke(this);
            }
        }

        void ClientTick()
        {
            MatchDirector director = MatchDirector.Current;
            if (director == null || !director.IsPlaying) return;

            // Between updates the client drains at the same rate the server does, so the readout
            // moves continuously instead of stepping once a second. The published value is still
            // the truth: it snaps this back into line on arrival.
            _displayed = Mathf.Max(0f, _displayed - _config.DrainPerSecond * Time.deltaTime);
        }

        // ==================================================================================
        //  Server-only mutation
        // ==================================================================================

        /// <summary>Adds life, capped. Used by fruit; server-only.</summary>
        public void ServerAdd(float seconds)
        {
            if (!IsServer || seconds <= 0f || !IsAlive) return;

            _exactLife = Mathf.Min(_config.MaxLife, _exactLife + seconds);

            // Published at once so the pickup reads as instant. Waiting for the interval would make
            // fruit feel unresponsive in the one moment the player is looking for feedback.
            _sinceLastPublish = 0f;
            _life.Value = _exactLife;
        }

        /// <summary>Resets for a new round. Server-only.</summary>
        public void ServerReset()
        {
            if (!IsServer) return;

            _reportedDeath = false;
            _exactLife = _config.StartingLife;
            _sinceLastPublish = 0f;
            _life.Value = _exactLife;
            _alive.Value = true;
        }
    }
}
