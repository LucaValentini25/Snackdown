using Snackdown.Gameplay.Player;
using Snackdown.Input;
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
    /// <para>Panning is clamped by <see cref="ArenaBounds"/>, so a small arena locks the camera in
    /// place and a large one lets it roam. The same component covers both without asking the level
    /// designer which kind they built.</para>
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public class SpectatorCamera : MonoBehaviour
    {
        [Tooltip("Units per second the camera pans while spectating.")]
        [SerializeField] float _panSpeed = 12f;

        [Tooltip("Seconds the camera takes to settle when control changes hands.")]
        [SerializeField] float _smoothing = 0.15f;

        [Tooltip("Reads the pan axis. Added automatically if left empty.")]
        [SerializeField] SpectatorInput _input;

        Camera _camera;
        Vector3 _restingPosition;
        Vector2 _target;
        Vector2 _velocity;
        bool _wasSpectating;

        /// <summary>True while this machine's player is out and free to look around.</summary>
        public bool IsSpectating { get; private set; }

        void Awake()
        {
            _camera = GetComponent<Camera>();
            _restingPosition = transform.position;
            _target = _restingPosition;

            if (_input == null) _input = GetComponent<SpectatorInput>();
            if (_input == null) _input = gameObject.AddComponent<SpectatorInput>();
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
            }

            if (IsSpectating) _target += _input.Pan * (_panSpeed * Time.deltaTime);
            else _target = _restingPosition;

            _target = ClampToArena(_target);

            Vector2 smoothed = Vector2.SmoothDamp(transform.position, _target, ref _velocity, _smoothing);
            transform.position = new Vector3(smoothed.x, smoothed.y, _restingPosition.z);
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
