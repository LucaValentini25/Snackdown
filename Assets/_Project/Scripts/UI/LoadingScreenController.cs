using Snackdown.Gameplay.Match;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Snackdown.UI
{
    /// <summary>
    /// Covers the screen while the arena loads, and takes the lobby down with it.
    /// </summary>
    /// <remarks>
    /// <para>Lives in the bootstrap scene, which is what lets it outlast both the lobby and the
    /// arena. A loading screen inside the lobby would be unloaded by the very transition it exists
    /// to cover.</para>
    /// <para>It also owns unloading the menu. The lobby scene is deliberately excluded from NGO's
    /// scene synchronization — it is local interface, not match state — which means nothing on the
    /// network will ever unload it. Without this, starting a match left the menu sitting on top of
    /// the arena saying "Loading the arena…" forever, which is exactly what happened.</para>
    /// <para>Progress comes from the server's count of who has finished loading, not from this
    /// peer's own load. A bar showing local progress would sit full while waiting for someone
    /// else, which reads as a freeze rather than as waiting for another player.</para>
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

        MatchDirector _director;
        MatchPhase _lastPhase = MatchPhase.Lobby;

        void Awake() => _document = GetComponent<UIDocument>();

        void OnEnable()
        {
            VisualElement root = _document.rootVisualElement;

            _root = root.Q<VisualElement>("loading-root");
            _title = root.Q<Label>("loading-title");
            _detail = root.Q<Label>("loading-detail");
            _countdown = root.Q<Label>("countdown");
            _fill = root.Q<VisualElement>("progress-fill");

            Hide();
        }

        void Update()
        {
            // The director is a networked object, so it does not exist until a session starts.
            if (_director == null)
            {
                _director = MatchDirector.Current;
                if (_director == null) return;
            }

            if (_director.Phase != _lastPhase)
            {
                OnPhaseChanged(_director.Phase);
                _lastPhase = _director.Phase;
            }

            if (_director.Phase == MatchPhase.Loading) UpdateProgress();
            else if (_director.Phase == MatchPhase.Countdown) UpdateCountdown();
        }

        void OnPhaseChanged(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Loading:
                    Show();
                    UnloadMenu();
                    break;

                case MatchPhase.Playing:
                    Hide();
                    break;

                case MatchPhase.Lobby:
                    Hide();
                    ReloadMenu();
                    break;
            }
        }

        void Show()
        {
            _root.RemoveFromClassList("hidden");
            _title.text = "Loading arena";
            _countdown.text = string.Empty;
            SetFill(0f);
        }

        void Hide() => _root.AddToClassList("hidden");

        void UpdateProgress()
        {
            SetFill(_director.LoadProgress);

            _detail.text = _director.ExpectedPeers > 1
                ? $"{_director.LoadedPeers} of {_director.ExpectedPeers} players ready"
                : string.Empty;
        }

        void UpdateCountdown()
        {
            // Full bar, because loading is done — the wait is now the countdown, and a bar stuck
            // short of the end would suggest something is still missing.
            SetFill(1f);
            _title.text = "Starting";
            _detail.text = string.Empty;

            _countdown.text = Mathf.CeilToInt(_director.CountdownRemaining).ToString();
        }

        void SetFill(float value01) => _fill.style.width = Length.Percent(Mathf.Clamp01(value01) * 100f);

        void UnloadMenu()
        {
            Scene menu = SceneManager.GetSceneByName(_menuScene);
            if (menu.IsValid() && menu.isLoaded) SceneManager.UnloadSceneAsync(menu);
        }

        void ReloadMenu()
        {
            if (!SceneManager.GetSceneByName(_menuScene).isLoaded)
                SceneManager.LoadScene(_menuScene, LoadSceneMode.Additive);
        }
    }
}
