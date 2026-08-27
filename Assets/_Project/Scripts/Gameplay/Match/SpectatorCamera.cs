using System.Collections.Generic;
using Snackdown.Gameplay.Player;
using Snackdown.Input;
using Snackdown.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// Hands the camera to a player once they are out of the round, and takes it back when they
    /// are not.
    /// </summary>
    /// <remarks>
    /// <para><b>Entirely local, and deliberately so.</b> Nothing here is replicated, because where
    /// someone is looking changes no outcome. Sending it would be traffic spent on a value no other
    /// peer can act on, and a second thing that can desynchronise for no benefit.</para>
    /// <para>Written as a reconciler: every frame it asks "should I be spectating?" and matches
    /// that, rather than reacting to a death event. Events are what left the loading screen stuck
    /// on clients that joined while the phase was already past — a listener that misses its one
    /// notification stays wrong forever, while a check that runs every frame corrects itself.</para>
    /// <para><b>It follows somebody rather than offering a free camera.</b> A player who just died
    /// wants to watch the fight, and free-look asks them to go and find it first. So the camera
    /// picks a survivor and tracks them, and a tap left or right moves to the next — the convention
    /// every game with a spectator already uses, so it needs no explaining. Free panning is what
    /// happens when there is nobody left to follow, which is the end of a round.</para>
    /// <para>Panning and following are both clamped by <see cref="ArenaBounds"/>, so a small arena
    /// locks the camera in place and a large one lets it roam. The same component covers both
    /// without asking the level designer which kind they built. Arena01 is the small kind: the clamp
    /// leaves about half a unit of slack, so following is correct there and barely visible.</para>
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public class SpectatorCamera : MonoBehaviour
    {
        [Tooltip("Units per second the camera pans while there is nobody to follow.")]
        [SerializeField] float _panSpeed = 12f;

        [Tooltip("Seconds the camera takes to settle when control changes hands.")]
        [SerializeField] float _smoothing = 0.15f;

        [Tooltip("Reads the pan axis. Added automatically if left empty.")]
        [SerializeField] SpectatorInput _input;

        /// <summary>How far the axis must be pushed before it counts as a request to switch.</summary>
        /// <remarks>
        /// Half deflection, and only on the frame it crosses: the same axis pans when nobody is left
        /// to follow, so a stick resting slightly off centre must not cycle through the roster.
        /// </remarks>
        const float SwitchThreshold = 0.5f;

        Camera _camera;
        Vector3 _restingPosition;
        Vector2 _target;
        Vector2 _velocity;
        bool _wasSpectating;
        int _heldSwitchDirection;

        readonly SpectatorTargetRing _ring = new SpectatorTargetRing();
        readonly List<ulong> _watchable = new List<ulong>();
        readonly List<Transform> _bodies = new List<Transform>();

        /// <summary>True while this machine's player is out and watching somebody else.</summary>
        public bool IsSpectating { get; private set; }

        /// <summary>True while the camera has a player to follow, rather than a free view.</summary>
        public bool IsWatchingSomeone { get; private set; }

        /// <summary>Who the camera is following. Meaningless unless <see cref="IsWatchingSomeone"/>.</summary>
        public ulong WatchedClientId { get; private set; }

        /// <summary>The camera on this peer, for the views that want to name who is being watched.</summary>
        /// <remarks>
        /// One per scene, like <see cref="ArenaBounds.Current"/> and <c>MatchDirector.Current</c>.
        /// A process-wide static is the wrong shape for anything replicated — the Play mode harness
        /// runs two peers in one process — but a camera belongs to a screen, and there is exactly
        /// one of those however many peers are pretending to be machines.
        /// </remarks>
        public static SpectatorCamera Current { get; private set; }

        void Awake()
        {
            _camera = GetComponent<Camera>();
            _restingPosition = transform.position;
            _target = _restingPosition;

            if (_input == null) _input = GetComponent<SpectatorInput>();
            if (_input == null) _input = gameObject.AddComponent<SpectatorInput>();

            Current = this;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        void LateUpdate()
        {
            IsSpectating = ShouldSpectate();

            // Entering spectator mode starts from wherever the camera already is, so the transition
            // is a pan rather than a cut. A cut here reads as a bug: the player just died, and the
            // first thing they would see is the world jumping.
            if (IsSpectating != _wasSpectating)
            {
                _wasSpectating = IsSpectating;
                _target = transform.position;
                _velocity = Vector2.zero;

                if (!IsSpectating) _ring.Clear();
            }

            if (IsSpectating) FollowOrPan();
            else
            {
                IsWatchingSomeone = false;
                _target = _restingPosition;
            }

            _target = ClampToArena(_target);

            Vector2 smoothed = Vector2.SmoothDamp(transform.position, _target, ref _velocity, _smoothing);
            transform.position = new Vector3(smoothed.x, smoothed.y, _restingPosition.z);
        }

        /// <remarks>
        /// The switch is read before the target is, so pressing left and having the camera move this
        /// frame rather than the next. Both paths end at <see cref="SpectatorTargetRing.Refresh"/>,
        /// which is what keeps the choice pointing at somebody who still exists.
        /// </remarks>
        void FollowOrPan()
        {
            CollectWatchablePlayers();

            int switchDirection = ReadSwitchRequest();
            if (switchDirection != 0) _ring.Step(_watchable, switchDirection);

            IsWatchingSomeone = _ring.Refresh(_watchable);

            if (!IsWatchingSomeone)
            {
                // Nobody left to follow — the end of a round. The axis goes back to being a pan, so
                // the losing screen is at least something the player can look around.
                _target += _input.Pan * (_panSpeed * Time.deltaTime);
                return;
            }

            WatchedClientId = _ring.Current;

            Transform body = _bodies[IndexOfWatched()];
            if (body != null) _target = body.position;
        }

        int IndexOfWatched()
        {
            for (int i = 0; i < _watchable.Count; i++)
                if (_watchable[i] == WatchedClientId) return i;

            return 0;
        }

        /// <remarks>
        /// Edge-triggered rather than level-triggered: holding the stick left should move one player
        /// left, not scroll through the roster at frame rate.
        /// </remarks>
        int ReadSwitchRequest()
        {
            float axis = _input.Pan.x;
            int pushed = Mathf.Abs(axis) >= SwitchThreshold ? (int)Mathf.Sign(axis) : 0;

            int request = pushed != 0 && pushed != _heldSwitchDirection ? pushed : 0;
            _heldSwitchDirection = pushed;

            return request;
        }

        /// <summary>
        /// The characters worth watching on this peer: alive, spawned, in roster order.
        /// </summary>
        /// <remarks>
        /// <para>Built from the characters rather than from the sessions, because a session is not
        /// in the world and has no transform to follow — and on a client the session does not even
        /// know which object its avatar is, since it is the server that spawns it. Aliveness still
        /// comes from the session, which is where it is replicated.</para>
        /// <para>Filtered by <c>NetworkManager</c>. The simulation loop's registry is one static list
        /// per process, which is right in a build and wrong under the Play mode harness, where two
        /// peers share the process and would otherwise offer each other's characters to watch.</para>
        /// <para>Sorted by owner id so the order is the same one the strip along the bottom shows.
        /// Pressing right and having the camera move to somebody who is not the next face along
        /// would be worse than not sorting at all.</para>
        /// </remarks>
        void CollectWatchablePlayers()
        {
            _watchable.Clear();
            _bodies.Clear();

            NetworkManager local = NetworkManager.Singleton;
            if (local == null) return;

            IReadOnlyList<IPredictedPeer> peers = NetworkSimulationLoop.ActivePlayers;

            for (int i = 0; i < peers.Count; i++)
            {
                if (peers[i] is not PredictedPlayer character) continue;
                if (character.NetworkManager != local || !character.IsSpawned) continue;

                PlayerSession session = PlayerSession.Of(local, character.OwnerClientId);
                if (session == null || session.Life == null || !session.Life.IsAlive) continue;

                InsertByOwnerId(character.OwnerClientId, character.transform);
            }
        }

        /// <remarks>
        /// An insertion sort over at most four entries, which is cheaper than allocating a
        /// comparison delegate every frame to sort a list that is nearly always already ordered.
        /// </remarks>
        void InsertByOwnerId(ulong clientId, Transform body)
        {
            int at = _watchable.Count;
            while (at > 0 && _watchable[at - 1] > clientId) at--;

            _watchable.Insert(at, clientId);
            _bodies.Insert(at, body);
        }

        /// <remarks>
        /// A match that has ended puts everyone in spectator, winner included: the round is over,
        /// there is nothing left to control, and letting the survivor keep walking around while the
        /// end screen declares them the winner reads as the match not having ended at all.
        /// </remarks>
        bool ShouldSpectate()
        {
            MatchDirector director = MatchDirector.Current;
            if (director == null) return false;
            if (director.Phase == MatchPhase.Ended) return true;
            if (!director.IsPlaying) return false;

            PlayerLife mine = LocalPlayerLife();
            return mine != null && !mine.IsAlive;
        }

        static PlayerLife LocalPlayerLife()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsClient) return null;

            for (int i = 0; i < PlayerLife.All.Count; i++)
                if (PlayerLife.All[i].IsOwner) return PlayerLife.All[i];

            return null;
        }

        Vector2 ClampToArena(Vector2 desired)
        {
            ArenaBounds bounds = ArenaBounds.Current;
            if (bounds == null || !_camera.orthographic) return desired;

            var halfExtents = new Vector2(_camera.orthographicSize * _camera.aspect, _camera.orthographicSize);
            return bounds.Clamp(desired, halfExtents);
        }
    }
}
