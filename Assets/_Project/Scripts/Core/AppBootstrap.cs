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

            // Guard against loading it twice: entering play mode from the lobby scene itself is a
            // normal thing to do while developing, and a second copy means two menus and two
            // cameras fighting over the screen.
            if (SceneManager.GetSceneByName(_firstScene).isLoaded) return;

            SceneManager.LoadScene(_firstScene, LoadSceneMode.Additive);
        }
    }
}
