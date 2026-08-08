using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Snackdown.Core
{
    /// <summary>
    /// Brings up the first playable screen once the bootstrap scene has loaded.
    /// </summary>
    /// <remarks>
    /// <para>Bootstrap deliberately holds nothing a player can see — it is the connection, the
    /// roster and the match director, and it stays loaded for the whole session. Something has to
    /// put the lobby on top of it, or starting the game lands on an empty scene with no way to do
    /// anything, which is exactly what happened.</para>
    /// <para>Loaded with Unity's own SceneManager rather than NGO's, because at this point there is
    /// no session: nobody is connected, so there is nobody to synchronize the load with. Once a
    /// match starts, <c>MatchDirector</c> takes over and every load goes through the network.</para>
    /// </remarks>
    public class AppBootstrap : MonoBehaviour
    {
        [Tooltip("Scene shown on startup. Must be in Build Settings.")]
        [SerializeField] string _firstScene = "Lobby";

        void Start()
        {
            if (string.IsNullOrWhiteSpace(_firstScene)) return;

            // Deliberately does NOT load the menu. LoadingScreenController owns that scene: it
            // decides when the menu should be up based on the match phase, and two components
            // loading the same scene is how two lobbies ended up stacked on top of each other.
            // This one only keeps it out of NGO's synchronization.
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted += ExcludeMenuFromSync;
                NetworkManager.Singleton.OnClientStarted += ExcludeMenuFromSync;
            }
        }

        void OnDestroy()
        {
            if (NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnServerStarted -= ExcludeMenuFromSync;
            NetworkManager.Singleton.OnClientStarted -= ExcludeMenuFromSync;
        }

        /// <summary>
        /// Keeps the menu scene out of NGO's scene synchronization.
        /// </summary>
        /// <remarks>
        /// <para>On connecting, NGO makes a client load whatever scenes the server has. The server
        /// has the menu loaded — every peer loads it on startup, before there is any session — so a
        /// joining client was told to load a scene it already had. The result was two menus, two
        /// cameras and two controllers in one process, and a handshake that failed while
        /// reconciling them.</para>
        /// <para>The menu is local interface, not match state. Nothing in it is shared, so nothing
        /// in it should be synchronized. Arenas still go through NGO, which is where synchronized
        /// loading genuinely matters.</para>
        /// </remarks>
        void ExcludeMenuFromSync()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.SceneManager == null) return;

            manager.SceneManager.VerifySceneBeforeLoading = (index, sceneName, mode) => sceneName != _firstScene;
        }
    }
}
