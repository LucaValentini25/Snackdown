using Unity.Netcode;
using UnityEngine;

namespace Snackdown.Gameplay.Match
{
    /// <summary>
    /// Starts a one-player match the moment Play is pressed, so the game can be looked at without
    /// going through the menu, the lobby or a network service.
    /// </summary>
    /// <remarks>
    /// <para><b>It uses the real spawn path.</b> It starts a host and drives
    /// <see cref="MatchDirector"/> through the phases a real match goes through, so the character is
    /// spawned by NGO exactly as it is online. A sandbox that instantiated the prefab directly would
    /// be quicker and would lie: the sizing bugs this was built for were only visible on the spawned
    /// copy, and a harness that skips the spawn cannot show them.</para>
    /// <para>It lives beside the director rather than in the bootstrap layer because driving a match
    /// is what it does, and putting it there would have made the app's startup assembly depend on
    /// the game's rules for the sake of a QA aid.</para>
    /// <para>Single peer on purpose. It is for looking at art and layout, where one character is
    /// enough. Anything that crosses the wire still needs two peers — see <c>docs/05</c>.</para>
    /// </remarks>
    public class SandboxRunner : MonoBehaviour
    {
        [Tooltip("Arena to open, as an index into the director's catalog.")]
        [SerializeField] int _arenaIndex;

        [Tooltip("Seconds to wait for the networked objects to spawn before starting the match.")]
        [SerializeField] float _startDelay = 0.25f;

        float _elapsed;
        bool _started;

        void Start()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[Snackdown] The sandbox needs the Bootstrap scene loaded for its NetworkManager.", this);
                enabled = false;
                return;
            }

            if (!NetworkManager.Singleton.IsListening) NetworkManager.Singleton.StartHost();
        }

        void Update()
        {
            if (_started) return;

            // The director is a networked object, so it does not exist on the frame the host starts.
            // Waiting a moment beats a null check that silently never fires.
            _elapsed += Time.deltaTime;
            if (_elapsed < _startDelay) return;

            MatchDirector director = MatchDirector.Current;
            if (director == null || !director.IsServer) return;

            _started = true;
            director.ServerStartMatch(_arenaIndex);
        }

        /// <remarks>
        /// Leaving a host running into the next Play session leaks the native socket and costs an
        /// editor restart — the pitfall <c>docs/05</c> records. Shutting down here is what makes the
        /// sandbox safe to enter and leave repeatedly, which is the whole point of it.
        /// </remarks>
        void OnDisable()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
        }
    }
}
