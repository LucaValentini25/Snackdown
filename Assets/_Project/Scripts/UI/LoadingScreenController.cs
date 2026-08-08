using Snackdown.Gameplay.Match;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Snackdown.UI
{
    /// <summary>
    /// Keeps what is on screen agreeing with the match phase: the menu during the lobby, a loading
    /// screen while the arena loads, and neither once play begins.
    /// </summary>
    /// <remarks>
    /// <para>Written as a reconciler, not as a listener. An earlier version reacted to phase
    /// <i>changes</i> — <c>if (phase != lastPhase)</c> — and a client that missed the moment stayed
    /// wrong forever: it sat in the lobby with the match already running underneath, its character
    /// moving on the host's screen. A transition can be missed in ordinary operation. The director
    /// is a networked object that does not exist until the session spawns it, so a client can meet
    /// it already several phases in; two changes can also land in one frame.</para>
    /// <para>So nothing here asks what changed. Every frame it asks what the phase is and makes the
    /// screen match, which is the same reasoning as reconciliation in the netcode layer: how you
    /// arrived does not matter, only where you should be.</para>
    /// <para>It lives in the bootstrap scene, which is what lets it outlast both the lobby it
    /// unloads and the arena it waits for.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public class LoadingScreenController : MonoBehaviour
    {
        [Tooltip("Menu scene to hide while a match is running. Must match AppBootstrap.")]
        [SerializeField] string _menuScene = "Lobby";

        UIDocument _document;
        VisualElement _root;
        Label _title;
        Label _detail;
        Label _countdown;
        VisualElement _fill;

        void Awake() => _document = GetComponent<UIDocument>();

        void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;

            _root = root.Q<VisualElement>("loading-root");
            _title = root.Q<Label>("loading-title");
            _detail = root.Q<Label>("loading-detail");
            _countdown = root.Q<Label>("countdown");
            _fill = root.Q<VisualElement>("progress-fill");
        }

        void Update()
        {
            MatchDirector director = MatchDirector.Current;

            // No session, or none yet: the menu is the right thing to be looking at.
            if (director == null)
            {
                SetLoadingVisible(false);
                EnsureMenuLoaded();
                return;
            }

            switch (director.Phase)
            {
                case MatchPhase.Lobby:
                case MatchPhase.Ended:
                    SetLoadingVisible(false);
                    EnsureMenuLoaded();
                    break;

                case MatchPhase.Loading:
                    SetLoadingVisible(true);
                    ShowProgress(director);

                    // The menu is left alone here on purpose. Unloading a scene by hand while NGO
                    // is synchronizing a networked load interferes with that operation, and the
                    // loading screen already covers it.
                    break;

                case MatchPhase.Countdown:
                    SetLoadingVisible(true);
                    ShowCountdown(director);
                    EnsureMenuUnloaded();
                    break;

                case MatchPhase.Playing:
                    SetLoadingVisible(false);
                    EnsureMenuUnloaded();
                    break;
            }
        }

        void SetLoadingVisible(bool visible) => _root.EnableInClassList("hidden", !visible);

        void ShowProgress(MatchDirector director)
        {
            _title.text = "Loading arena";
            _countdown.text = string.Empty;
            SetFill(director.LoadProgress);

            _detail.text = director.ExpectedPeers > 1
                ? $"{director.LoadedPeers} of {director.ExpectedPeers} players ready"
                : string.Empty;
        }

        void ShowCountdown(MatchDirector director)
        {
            // Full bar: loading is done, and a bar stuck short of the end would suggest otherwise.
            SetFill(1f);
            _title.text = "Starting";
            _detail.text = string.Empty;
            _countdown.text = Mathf.CeilToInt(director.CountdownRemaining).ToString();
        }

        void SetFill(float value01) => _fill.style.width = Length.Percent(Mathf.Clamp01(value01) * 100f);

        /// <summary>A scene load or unload this component started and is still waiting on.</summary>
        /// <remarks>
        /// The missing piece in the first reconciler. Loading a scene additively does not finish
        /// within the frame that asks for it, so "is the menu loaded?" keeps answering no while a
        /// load is already on its way — and a reconciler that only compares desired against actual
        /// fires another one every frame until the first lands. That produced two lobby scenes,
        /// and unloading then removed only one of them, leaving the other covering the arena.
        /// <para>Reconciling has to account for corrections that are already in flight, not just
        /// for the difference between where things are and where they should be.</para>
        /// </remarks>
        AsyncOperation _sceneOperation;

        bool SceneOperationInFlight => _sceneOperation != null && !_sceneOperation.isDone;

        void EnsureMenuUnloaded()
        {
            if (SceneOperationInFlight) return;

            // Every copy, not the first: GetSceneByName returns one, and an earlier version of this
            // file could leave more than one loaded.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene.name != _menuScene) continue;

                _sceneOperation = SceneManager.UnloadSceneAsync(scene);
                return;
            }
        }

        void EnsureMenuLoaded()
        {
            if (SceneOperationInFlight) return;
            if (CountLoaded(_menuScene) > 0) return;

            _sceneOperation = SceneManager.LoadSceneAsync(_menuScene, LoadSceneMode.Additive);
        }

        static int CountLoaded(string sceneName)
        {
            int count = 0;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.name == sceneName) count++;
            }

            return count;
        }
    }
}
